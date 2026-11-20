# Workflow store reference-host benchmark

`run-host.py` starts the selected checkout's Release/net10.0 `Flamme.Server.Web.dll` on Kestrel and creates a fresh PostgreSQL 16 container for it. Run one version at a time on a machine with Python 3.8+, the .NET 10 runtime, Docker, and a local `postgres:16` image. Build the reference host in each target checkout with its pinned SDK before running trials.

```sh
dotnet build src/apps/Flamme.Server.Web/Flamme.Server.Web.csproj -c Release -f net10.0
```

From the checkout containing the benchmark, set `tree` to the checkout whose compiled server you want to measure. This example runs the short smoke workload.

```sh
tree=../main
bench=scripts/benchmarks/workflow-store
results=$(mktemp -d)
BENCH_LABEL=main BENCH_OUTPUT="$results/trials.jsonl" \
  python3 "$bench/run-host.py" --tree "$tree" --state "$results/host" --timeout 900 -- \
  python3 -B "$bench/run.py" --smoke
```

For a measured run, omit `--smoke`. Use a fresh results directory and a revision-specific label for each run. The launcher executes the workload in the caller's working directory. Its exit status propagates to the caller. An expired workload exits with an error. Bootstrap has separate startup timeouts.

## Stock Elsa control

The standalone comparison uses the public Elsa 3.10.2 packages, the default local runtime, and a migrated PostgreSQL 16 database. It runs the same held-row workflow resume on the stock event endpoint. The fixture includes the lock file, run commands, and raw trial results. [Stock Elsa benchmark](https://github.com/DenDeline/flamme-contributions/tree/90361d7a95da9a34990f0f7e58ab8f4a292c334a/upstream) · [Raw comparison data](https://github.com/DenDeline/flamme-contributions/tree/bench/96-elsa-postgres/results)

## What readiness establishes

The original server registrations apply the management and runtime migrations. The launcher polls `/health/ready`, checks every migration ID in the selected tree against PostgreSQL history, then passes a live fixture to the workload. During bootstrap it also checks for HTTP 401 on an anonymous instance list and signs in a temporary administrator for each reference tenant. Each administrator must successfully list workflow instances. Timed operations use the default tenant.

Both ports bind to loopback. The launcher copies the reference configuration into a private content root, supplies the local connection, origin and signing key, and configures temporary users through the host's identity provider. Development mode enables the reference single-host file-lock provider. Each process has its own `App_Data` directory.

## Inspect a running fixture

Omit the workload command to keep the host available for up to `--timeout` seconds. The launcher prints the path to `ready.json`. It includes the API origin, database container ID, image ID, server version, migration IDs, and paths to private authorization files. Each authorization file contains a complete bearer header value. The launcher sets `FLAMME_BENCH_FIXTURE` for a workload child. A separate workload process can set it to the same manifest path.

Diagnostic SQL can run through `docker exec -i <container-id> psql -X -v ON_ERROR_STOP=1 -U postgres -d elsa`. The database uses Docker's local socket authentication for this command. Use a fresh `--state` path with an existing parent directory. Normal completion, SIGINT, SIGTERM, and workload timeout release this launcher's processes, container, anonymous database volume, manifest and private credentials. The state directory retains `host.log` for diagnosis.
