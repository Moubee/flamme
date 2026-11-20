# PostgreSQL workflow benchmarks

Run from the repository root with the SDK in `global.json`, the .NET 10 runtime, and a local Docker engine. Each BenchmarkDotNet case creates its own PostgreSQL 16 container and removes it after validation. The HTTP cases also start the reference server on Kestrel, check its management and runtime migrations, and authenticate through its identity API. Database creation, host startup and login run outside the measured operations.

```sh
project=test/performance/Flamme.Workflows.PerformanceTests
dotnet run -c Release --project "$project" -- --filter '*WorkflowStore*' --list flat
dotnet run -c Release --project "$project" -- --filter '*StoreBenchmarks*' '*WorkflowHttpBenchmarks*' --job Dry
```

The Dry job checks that the workload runs and its database assertions pass. For performance measurements, run the desired filter without `--job Dry` on an idle machine. BenchmarkDotNet writes statistics and managed allocations to `BenchmarkDotNet.Artifacts/results/`. Keep the commit, PostgreSQL image, runtime, machine configuration and job settings with each result.

## Store calls

`StoreBenchmarks` measures the registered workflow instance store updating existing rows. Each call alternates between two checkpoint revisions so PostgreSQL receives a change. The input is a captured 1,858-byte suspended event workflow with a typed bookmark. The serializer control uses the registered workflow-state serializer and storage codec on the same input.

| Method | Unit reported |
| --- | --- |
| `SaveSingle` | One committed `SaveAsync` |
| `SaveBatch1`, `SaveBatch50`, `SaveBatch500` | One committed `SaveManyAsync` of the indicated size |
| `SerializeOne` | One serialization and encoding of that checkpoint |

Cleanup opens a fresh scope to verify the final rows and their stored checkpoints. Batch timings and allocations cover the whole call. Divide by the batch size for a per-instance figure.

## Independent writes during a row lock

`WorkflowHttpBenchmarks` prepares 49 real workflows per iteration. It times a burst of 48 peer requests on either the synchronous event-resume route or the multipart import route. Each route has a control and a held-row case. For the latter, a separate PostgreSQL transaction locks the anchor row and the benchmark confirms its writer is waiting before it starts the peers. At one second, an independent query counts durable peer commits and verifies that the anchor row is still unchanged. HTTP completions are counted separately. The run log records the peer count and request latency percentiles. BenchmarkDotNet reports elapsed time for the whole peer burst.

For historical comparison, the [stock Elsa control](https://github.com/DenDeline/flamme-contributions/tree/90361d7a95da9a34990f0f7e58ab8f4a292c334a/upstream) and [issue #96 measurements](https://github.com/Moubee/flamme/issues/96#issuecomment-7047426069) describe the original workloads and tested revisions.

## Reproduce the million-workflow lifecycle

`WorkflowLifecycleBenchmarks` builds the full suspended backlog with up to 48 HTTP callers, then resumes the same IDs with up to 80. It uses the same Kestrel host, PostgreSQL fixture and synchronous event route as the held-row case. A database audit reads every expected ID in batches of 1,024 after each phase, checking the saved checkpoint and bookmark. The job reports time for the resume phase. The run log records both phase durations.

```sh
FLAMME_BENCH_WORKFLOW_COUNT=256 dotnet run -c Release --project "$project" -- \
  --filter '*WorkflowLifecycleBenchmarks*' --job Dry

FLAMME_BENCH_WORKFLOW_COUNT=1000000 dotnet run -c Release --project "$project" -- \
  --filter '*WorkflowLifecycleBenchmarks*' --job Dry
```

The count explicitly enables this campaign. Use the single-iteration Dry job for the large run. Allow for PostgreSQL data, indexes and WAL on the Docker host. The previous million-workflow campaign generated approximately 97 GiB of cumulative WAL across both phases.
