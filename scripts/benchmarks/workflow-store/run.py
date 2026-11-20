"""Drive a live reference server. Set BENCH_LABEL, BENCH_OUTPUT and FLAMME_BENCH_FIXTURE."""

import argparse
import json
import math
import os
import socket
import threading
import time
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from observer import await_writer, committed, literal, lock_instance, session


class Api:
    def __init__(self, url, authorization=None):
        self.url = url.rstrip("/")
        self.authorization = authorization
        if not authorization:
            login = self.call("/identity/login", {
                "username": os.getenv("BENCH_USER", "admin"),
                "password": os.getenv("BENCH_PASSWORD", "password"),
            })
            if not login["isAuthenticated"]:
                raise ValueError("Reference server rejected the benchmark login")
            self.authorization = "Bearer " + login["accessToken"]

    def call(self, path, body=None, content_type="application/json"):
        if body is not None and not isinstance(body, bytes):
            body = json.dumps(body).encode()
        headers = {"Content-Type": content_type}
        if self.authorization:
            headers["Authorization"] = self.authorization
        req = urllib.request.Request(self.url + path, data=body, headers=headers)
        with urllib.request.urlopen(req, timeout=30) as response:
            data = response.read()
            return json.loads(data) if data else None


def connect():
    if "FLAMME_BENCH_FIXTURE" in os.environ:
        fixture = json.loads(Path(os.environ["FLAMME_BENCH_FIXTURE"]).read_text())
        auth = Path(fixture["authorization_file"]).read_text().strip()
        psql = ["docker", "exec", "-i", fixture["container_id"], "psql",
                "-U", fixture["database_user"], "-d", fixture["database"]]
        return Api(fixture["api_url"], auth), psql
    api_url = os.environ["BENCH_URL"].rstrip("/") + os.getenv("BENCH_API_PREFIX", "/elsa/api")
    token = os.getenv("BENCH_TOKEN")
    return Api(api_url, "Bearer " + token if token else None), json.loads(os.environ["BENCH_PSQL"])


def create_instances(api, definition, count):
    def start(index):
        correlation = f"{batch}-{index}"
        state = api.call(f"/workflow-definitions/{definition}/execute",
                         {"correlationId": correlation})["workflowState"]
        return state["id"], correlation

    batch = uuid.uuid4().hex
    with ThreadPoolExecutor(8) as pool:
        instances = dict(pool.map(start, range(count)))
    if len(instances) != count:
        raise RuntimeError("Execute did not return distinct workflow IDs")
    return instances


def import_request(api, instance_id, correlation):
    exported = api.call("/bulk-actions/export/workflow-instances", {"ids": [instance_id]})
    state = exported["WorkflowState"]
    state["correlationId"] = correlation
    boundary = uuid.uuid4().hex
    content = json.dumps({"WorkflowState": state}).encode()
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="state.json"\r\n'
            'Content-Type: application/json\r\n\r\n').encode() + content + f'\r\n--{boundary}--\r\n'.encode()
    return "/bulk-actions/import/workflow-instances", body, "multipart/form-data; boundary=" + boundary


def timed(api, operation, request, gate=None):
    if gate:
        gate.wait()
    started = time.monotonic()
    error = None
    timed_out = False
    try:
        result = api.call(*request)
        if operation == "import" and (not result or result.get("imported") != 1):
            raise ValueError(f"Expected one imported instance, got {result!r}")
    except urllib.error.HTTPError as exc:
        error = f"HTTP {exc.code}: {exc.read(400).decode(errors='replace')}"
        timed_out = exc.code in (408, 504)
    except (OSError, ValueError) as exc:
        error = str(exc)
        timed_out = isinstance(getattr(exc, "reason", exc), (TimeoutError, socket.timeout))
    ended = time.monotonic()
    return {"end": ended, "ms": round(1000 * (ended - started), 3), "error": error, "timeout": timed_out}


def trial(api, observer, holder, definition, correlations, operation, held, requests):
    hot, *peers = correlations
    gate = threading.Barrier(len(peers) + 1, timeout=20)
    witness = None
    before = None
    released = None
    with ThreadPoolExecutor(len(correlations)) as pool:
        workers = [pool.submit(timed, api, operation, req, gate) for req in requests[1:]]
        try:
            if held:
                holder_pid = lock_instance(holder, hot)
            anchor = pool.submit(timed, api, operation, requests[0])
            if held:
                witness = await_writer(observer, holder_pid, anchor)
                confirmed = time.monotonic()
            gate.wait()
            if held:
                # Sample durable state near the end of the hold, before sending COMMIT.
                time.sleep(max(0, confirmed + 0.98 - time.monotonic()))
                before = committed(observer, definition, correlations, finished=True)
                sampled = time.monotonic()
                time.sleep(max(0, confirmed + 1 - sampled))
                released = time.monotonic()
                holder("COMMIT")
        except BaseException:
            gate.abort()
            raise
        finally:
            if held and released is None:
                holder("ROLLBACK")
        anchor_result = anchor.result()
        results = [future.result() for future in workers]

    final = committed(observer, definition, correlations, finished=True)
    missing = sorted(correlations.keys() - final)
    all_results = [anchor_result, *results]
    latencies = sorted(r["ms"] for r in results)
    errors = [dict(id=i, message=r["error"]) for i, r in zip(correlations, all_results) if r["error"]]
    row = {
        "operation": operation, "case": "held" if held else "control", "unrelated": len(peers),
        "anchor_id": hot, "anchor_ms": anchor_result["ms"],
        "p50_ms": latencies[math.ceil(0.50 * len(peers)) - 1],
        "p95_ms": latencies[math.ceil(0.95 * len(peers)) - 1],
        "p99_ms": latencies[math.ceil(0.99 * len(peers)) - 1],
        "errors": len(errors), "timeouts": sum(r["timeout"] for r in all_results),
        "error_details": errors, "committed": len(final), "audit_failed_ids": missing,
        "release_before_count": None, "committed_before_release": None,
    }
    if held:
        row.update(
            holder_pid=holder_pid, writer=witness, hold_ms=round(1000 * (released - confirmed), 3),
            audit_at_ms=round(1000 * (sampled - confirmed), 3),
            release_before_count=sum(r["end"] < released and not r["error"] for r in results),
            committed_before_release=len(before.intersection(peers)),
            anchor_pending_before_release=hot not in before and anchor_result["end"] >= released,
        )
    return row


def run(api, psql, iterations, peers, smoke, emit):
    definition = "store-bench-" + uuid.uuid4().hex
    model = json.loads(Path(__file__).with_name("event-workflow.json").read_text())
    model["definitionId"] = definition
    with session(psql) as observer, session(psql) as holder:
        server_version = int(observer("SHOW server_version_num")[0])
        if server_version // 10000 != 16:
            raise ValueError(f"Expected PostgreSQL 16, got {server_version}")
        api.call("/workflow-definitions", {"model": model, "publish": True})
        published = observer(f'''SELECT "Id" FROM "Elsa"."WorkflowDefinitions"
                                 WHERE "DefinitionId" = {literal(definition)} AND "IsPublished"''')
        if len(published) != 1:
            raise RuntimeError("Observer could not see the published definition")
        emit({"kind": "fixture", "definition": definition, "version_id": published[0], "pg_version": server_version})
        for index in range(iterations):
            phase = "smoke" if smoke else "warmup" if index == 0 else "measured"
            for held in (False, True):
                instances = create_instances(api, definition, peers + 1)
                if committed(observer, definition, instances, finished=False) != instances.keys():
                    raise RuntimeError("Expected each workflow to be committed with one suspended event bookmark")
                for operation in ("resume", "import"):
                    correlations = instances
                    if operation == "resume":
                        requests = [("/events/store-bench/trigger", {
                            "workflowInstanceId": i, "correlationId": c, "workflowExecutionMode": 0,
                        }) for i, c in instances.items()]
                    else:
                        correlations = {i: c + "-imported" for i, c in instances.items()}
                        with ThreadPoolExecutor(8) as pool:
                            requests = list(pool.map(lambda i: import_request(api, i, correlations[i]), instances))
                    row = trial(api, observer, holder, definition, correlations, operation, held, requests)
                    emit(dict(row, kind="trial", phase=phase, trial=index))
                    if row["errors"] or row["audit_failed_ids"] or not row.get("anchor_pending_before_release", True):
                        raise RuntimeError(f"{operation} failed, see JSONL trial")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--smoke", action="store_true", help="exercise both routes with two peers and no measured trials")
    args = parser.parse_args()
    label = os.environ["BENCH_LABEL"]
    with Path(os.environ["BENCH_OUTPUT"]).open("x") as output:
        def emit(row):
            print(json.dumps(dict(row, label=label), separators=(",", ":")), file=output, flush=True)

        try:
            api, psql = connect()
            run(api, psql, 1 if args.smoke else 4, 2 if args.smoke else 48, args.smoke, emit)
        except Exception as exc:
            emit({"kind": "invalid", "error": str(exc)})
            raise


if __name__ == "__main__":
    main()
