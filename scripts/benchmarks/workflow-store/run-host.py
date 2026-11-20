#!/usr/bin/env python3
"""Run a prebuilt Flamme.Server.Web and a fresh PostgreSQL 16 for one workload."""

import argparse
import base64
from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import build_opener, ProxyHandler, Request


def run(*args, **kwargs):
    return subprocess.run(args, check=True, capture_output=True, text=True,
                          timeout=kwargs.pop("timeout", 15), **kwargs).stdout.strip()


def wait_for(label, probe, seconds=90):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if probe():
            return
        time.sleep(0.25)
    raise RuntimeError(f"Timed out waiting for {label}")


def stop_child(child):
    # Each child owns a fresh process group, including any workload diagnostics.
    try:
        os.killpg(child.pid, signal.SIGTERM)
        child.wait(timeout=8)
    except subprocess.TimeoutExpired:
        os.killpg(child.pid, signal.SIGKILL)
        child.wait(timeout=5)
    except ProcessLookupError:
        pass


def get_http(url, authorization=None, body=None):
    headers = {"Content-Type": "application/json"} if body else {}
    if authorization:
        headers["Authorization"] = authorization
    request = Request(url, data=json.dumps(body).encode() if body else None, headers=headers)
    with build_opener(ProxyHandler({})).open(request, timeout=5) as response:
        return response.read()


def configure_host(app, private, origin, connection, password):
    config = json.loads((app / "appsettings.json").read_text())
    config["ConnectionStrings"]["PostgreSql"] = connection
    config["Http"]["BaseUrl"] = origin
    tenants = config["Multitenancy"]["Tenants"]
    for tenant in tenants:
        settings = tenant["Configuration"]
        settings["ConnectionStrings"]["PostgreSql"] = connection
        settings["Http"]["Host"] = origin.split("://")[1]

    # Same configuration-backed identity provider, PBKDF2 format and admin role as the host.
    salt = secrets.token_bytes(32)
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000)
    envelope = b"pbkdf2-sha256$600000$" + base64.b64encode(digest)
    config["Identity"]["Tokens"]["SigningKey"] = secrets.token_hex(48)
    users = [{
        "Id": secrets.token_hex(16), "Name": f"benchmark-{i}", "TenantId": tenant["Id"] or "", "Roles": ["admin"],
        "HashedPassword": base64.b64encode(envelope).decode(),
        "HashedPasswordSalt": base64.b64encode(salt).decode(),
    } for i, tenant in enumerate(tenants)]
    config["Identity"]["Users"] = users
    (private / "appsettings.json").write_text(json.dumps(config))
    if (app / "Workflows").is_dir():
        shutil.copytree(app / "Workflows", private / "Workflows")
    return users


def postgres_ready(container):
    # Wait for the final TCP listener. The image initializes the DB on a socket first.
    return subprocess.run(
        ["docker", "exec", container, "pg_isready", "-qh", "127.0.0.1", "-U", "postgres", "-d", "elsa"],
        capture_output=True, timeout=5).returncode == 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tree", required=True, type=Path, help="checkout with a Release/net10.0 build")
    parser.add_argument("--state", required=True, type=Path, help="new directory for the manifest and private log")
    parser.add_argument("--timeout", type=int, default=900, help="maximum workload/idle lifetime in seconds")
    parser.add_argument("command", nargs=argparse.REMAINDER, help="optional workload after --")
    args = parser.parse_args()
    if args.timeout < 1:
        parser.error("--timeout must be positive")
    tree = args.tree.resolve()
    app = tree / "src/apps/Flamme.Server.Web"
    dll = app / "bin/Release/net10.0/Flamme.Server.Web.dll"
    if not dll.is_file():
        parser.error(f"Missing Release/net10.0 build: {dll}")
    state = args.state.absolute()
    os.umask(0o077)
    state.mkdir(mode=0o700)
    ready = state / "ready.json"
    with ExitStack() as cleanup:
        private = Path(cleanup.enter_context(tempfile.TemporaryDirectory(dir=state)))
        db_password = secrets.token_hex(32)
        cidfile = private / "postgres.cid"

        def remove_postgres():
            if cidfile.is_file() and cidfile.read_text().strip():
                run("docker", "rm", "-fv", cidfile.read_text().strip(), timeout=30)

        cleanup.callback(remove_postgres)
        container = run("docker", "create", "--pull=never", "--cidfile", str(cidfile),
                        "--label", "org.flamme.benchmark=workflow-store",
                        "-p", "127.0.0.1::5432", "-e", "POSTGRES_PASSWORD", "-e", "POSTGRES_DB=elsa",
                        "postgres:16", env={**os.environ, "POSTGRES_PASSWORD": db_password}, timeout=120)
        run("docker", "start", container, timeout=30)
        wait_for("PostgreSQL TCP readiness", lambda: postgres_ready(container))
        db_port = int(run("docker", "port", container, "5432/tcp").split(":")[-1])
        psql = ["docker", "exec", "-i", container, "psql", "-U", "postgres", "-d", "elsa"]
        connection = f"Host=127.0.0.1;Port={db_port};Database=elsa;Username=postgres;Password={db_password}"
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            origin = f"http://127.0.0.1:{sock.getsockname()[1]}"
        api = origin + "/elsa/api"
        password = secrets.token_urlsafe(32)
        users = configure_host(app, private, origin, connection, password)
        host_env = {k: v for k, v in os.environ.items() if k in ("PATH", "DOTNET_ROOT", "LANG", "LC_ALL")}
        host_env.update(ASPNETCORE_URLS=origin, DOTNET_ENVIRONMENT="Development",
                        XDG_DATA_HOME=str(private), TMPDIR=str(private))
        log = cleanup.enter_context((state / "host.log").open("w"))
        host = subprocess.Popen(["dotnet", str(dll)], cwd=private, env=host_env,
                                stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        cleanup.callback(stop_child, host)

        def healthy():
            if host.poll() is not None:
                raise RuntimeError(f"Kestrel exited with status {host.returncode}. See {log.name}")
            try:
                return get_http(origin + "/health/ready") == b"Healthy"
            except (OSError, URLError):
                return False

        wait_for("Kestrel /health/ready", healthy)
        instances = api + "/workflow-instances?pageSize=1"
        try:
            get_http(instances)
        except HTTPError as error:
            if error.code != 401:
                raise
        else:
            raise RuntimeError("Anonymous management read succeeded")
        auth_files = {}
        for user in users:
            login = json.loads(get_http(api + "/identity/login", body={"username": user["Name"], "password": password}))
            if not login.get("isAuthenticated") or not login.get("accessToken"):
                raise RuntimeError(f"Reference identity login failed for {user['Name']}")
            authorization = "Bearer " + login["accessToken"]
            json.loads(get_http(instances, authorization))
            auth_file = private / (user["Name"] + ".auth")
            auth_file.write_text(authorization)
            auth_files[user["TenantId"]] = str(auth_file)

        migrations_dir = tree / "src/modules/Flamme.Persistence.EFCore.PostgreSql/Migrations"
        expected = {p.name.split(".Designer.cs")[0] for module in ("Management", "Runtime")
                    for p in (migrations_dir / module).glob("*.Designer.cs")}
        history = run(*psql, "-XAt", "-v", "ON_ERROR_STOP=1", "-c",
                      'SELECT "MigrationId" FROM "Elsa"."__EFMigrationsHistory"').splitlines()
        if not expected or not expected.issubset(history):
            raise RuntimeError(f"Missing reference migration IDs: {sorted(expected.difference(history))}")
        manifest = dict(base_url=origin, api_url=api, authorization_file=auth_files[""],
                        container_id=container, database="elsa", database_user="postgres",
                        tenant_authorization_files=auth_files, tenants=sorted(auth_files), migrations=sorted(history),
                        postgres_version=run(*psql, "-XAtc", "SHOW server_version"),
                        postgres_image=run("docker", "inspect", "-f", "{{.Image}}", container))
        pending = private / "ready.tmp"
        pending.write_text(json.dumps(manifest, indent=2) + "\n")
        pending.replace(ready)
        cleanup.callback(ready.unlink, missing_ok=True)
        print(f"Reference host ready. Fixture manifest {ready}", flush=True)

        command = args.command[1:] if args.command[:1] == ["--"] else args.command
        child = None
        if command:
            token = Path(auth_files[""]).read_text().split(" ", 1)[1]
            env = dict(os.environ, BENCH_URL=origin, BENCH_PSQL=json.dumps(psql), BENCH_TOKEN=token,
                       FLAMME_BENCH_FIXTURE=str(ready))
            child = subprocess.Popen(command, env=env, start_new_session=True)
            cleanup.callback(stop_child, child)
        expires = time.monotonic() + args.timeout
        while time.monotonic() < expires:
            if host.poll() is not None:
                raise RuntimeError(f"Kestrel exited with status {host.returncode}. See {log.name}")
            if child and child.poll() is not None:
                return child.returncode
            time.sleep(0.25)
        if child:
            raise RuntimeError("Workload exceeded --timeout")
    return 0


if __name__ == "__main__":
    signal.signal(signal.SIGTERM, signal.default_int_handler)
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
    except HTTPError as exc:
        print(f"Fixture error: HTTP {exc.code} from {exc.url}", file=sys.stderr)
        sys.exit(1)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as exc:
        print(f"Fixture error: {exc}", file=sys.stderr)
        if isinstance(exc, subprocess.CalledProcessError) and exc.stderr:
            print(exc.stderr.strip(), file=sys.stderr)
        sys.exit(1)
