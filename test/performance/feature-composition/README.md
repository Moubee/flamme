# Managed feature composition benchmarks

Run the BenchmarkDotNet suite from the repository root with the SDK selected by `global.json`.

```sh
project=test/performance/feature-composition/Elsa.FeatureComposition.Benchmarks.csproj
dotnet restore "$project" --locked-mode
dotnet run -c Release --project "$project" --no-restore -- --filter '*'
```

BenchmarkDotNet builds the same project with `ElsaExecutionMode=Dynamic` and `ElsaExecutionMode=Generated`. Each process checks its actual module implementation and workflow feature membership. The small graph selects `WorkflowsFeature` and its prerequisites. The web case uses the reference server’s `ReferenceServerComposition.Apply()`, Development configuration and service defaults. Both modes use the same managed .NET 11 runtime, server GC and tiered-compilation setting (`DOTNET_TieredCompilation=0`).

**First Apply.** Each of ten fresh processes per mode and graph prepares its service collection and configuration in `GlobalSetup`. A ColdStart job times one invocation that creates and configures the module and returns from its first `Apply()`. Startup timing excludes process launch, builder setup, DI provider construction, HTTP and database startup. The benchmark also reads precise process-wide managed allocation counters immediately before and after composition in that same invocation. It rejects a second call in the same process. Cleanup exports the allocated bytes and installed feature names. The runner verifies the feature lists agree across launches and modes.

**Warm operations.** After composition, BenchmarkDotNet measures one successful `HasFeature<WorkflowsFeature>()` call or one completed `IWorkflowRunner.RunAsync` on a prebuilt single-activity workflow. The workflow uses an in-process scheduler, activity invoker and no-op commit handler in both hosts. It verifies the activity executes once and the workflow finishes. This exercises the composed services without connecting to PostgreSQL or starting hosted services. `MemoryDiagnoser` records allocation per warm operation. The warm job uses three fresh processes per case, each with three warm-up iterations and eight timed iterations, with a 150 ms target per iteration.

GitHub Markdown and CSV reports are written under `BenchmarkDotNet.Artifacts/results`. Startup allocation samples are written separately under `BenchmarkDotNet.Artifacts/startup-*`. BenchmarkDotNet 0.15.8’s runtime identifiers end at .NET 10. This project uses its .NET CLI generator, builder and process executor with an explicit `net11.0` target and sequential builds. Each benchmark executes in the framework-dependent child process shown in the report.

For the separately published NativeAOT workloads, see the [NativeAOT comparison](native/README.md).

## Results on November 18, 2026

The host was an 18-core Apple M5 Pro with 48 GiB RAM, macOS 27.0.1 arm64, SDK 11.0.100 and .NET 11.0.0. BenchmarkDotNet ran the processes at normal priority. Each mode installed seven features on the small graph and 42 on the web graph. [BenchmarkDotNet measurement samples and first-invocation allocations](samples.csv)

### First Apply

Each cell shows the median [25th, 75th percentile] across ten processes.

| Graph (features) | Mode | Time (ms) | Allocated (bytes) |
|---|---|---:|---:|
| Small (7) | Dynamic | 23.50 [22.49, 23.77] | 260,920 [260,920, 260,920] |
| Small (7) | Generated | 20.81 [20.45, 21.13] | 221,144 [221,144, 221,144] |
| Web (42) | Dynamic | 105.24 [103.79, 115.92] | 6,838,056 [6,838,056, 6,855,348] |
| Web (42) | Generated | 103.82 [102.99, 104.89] | 6,714,228 [6,702,848, 6,725,792] |

### Warm lookup and workflow

Times show the BenchmarkDotNet mean ± the half-width of its 99.9% confidence interval. Sequential throughput is the reciprocal of mean workflow time. The launch range spans the mean retained iteration time in each of the three independent processes. The CSV contains individual retained workload measurements after BenchmarkDotNet overhead correction and outlier filtering.

| Graph | Operation | Mode | Time/op | Launch range | Bytes/op | Workflows/s |
|---|---|---|---:|---:|---:|---:|
| Small | HasFeature | Dynamic | 16.03 ± 0.14 ns | 15.87–16.17 ns | 0 | — |
| Small | HasFeature | Generated | 16.22 ± 0.17 ns | 16.03–16.39 ns | 0 | — |
| Small | RunWorkflow | Dynamic | 21.22 ± 0.20 µs | 21.09–21.47 µs | 23,786 | 47,125 |
| Small | RunWorkflow | Generated | 21.13 ± 0.15 µs | 20.96–21.23 µs | 23,786 | 47,322 |
| Web | HasFeature | Dynamic | 16.74 ± 0.95 ns | 15.77–18.33 ns | 0 | — |
| Web | HasFeature | Generated | 16.83 ± 0.77 ns | 16.09–18.11 ns | 0 | — |
| Web | RunWorkflow | Dynamic | 42.29 ± 0.24 µs | 42.14–42.53 µs | 35,702 | 23,647 |
| Web | RunWorkflow | Generated | 42.20 ± 0.32 µs | 41.90–42.36 µs | 35,702 | 23,697 |

Generated composition used 39,776 fewer median startup bytes on the small graph (15.2%) and 123,828 fewer on the web graph (1.8%). Median first Apply was 2.69 ms faster on the small graph. The web graph’s 1.42 ms difference fell within the overlapping startup interquartile ranges. The three-launch ranges overlap for both warm operations on each graph. Workflow allocation was identical across modes within each graph, and mean throughput differed by less than 0.5%.
