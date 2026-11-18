# Managed feature composition benchmark

From the repository root, run the script with Python 3.8+ and the .NET SDK in `global.json`. It restores locked packages, publishes both modes in Release and prints a JSON row per process, followed by medians and inclusive quartiles.

```sh
python3 test/performance/feature-composition/run.py --samples 10
```

The script copies tracked source into a temporary directory. It inserts the measurement boundary around the reference web server’s existing `AddElsa` registration and stops before the HTTP host starts. A second entry point creates a module and selects `WorkflowsFeature` plus its dependencies. Both graphs use the same executable in each build. Each row comes from a fresh `dotnet Elsa.Server.Web.dll` process. A fixed-seed shuffle changes the order of the four cases in each round.

- **Startup** measures wall-clock time and the process-wide managed allocations from just before module creation through the return from the first `Apply()`. Service collection setup, process launch, DI provider creation, HTTP startup and database connections fall outside that interval.
- **Lookup** measures 16 million successful `IModule.HasFeature<WorkflowsFeature>()` calls after 100,000 warm-up calls. Each process verifies its concrete provider and the harness compares the complete installed-feature names across modes.
- **Workflow throughput** measures 4,096 sequential `IWorkflowRunner.RunAsync` calls on one prebuilt graph after 1,024 warm-up runs. The workflow executes one `Inline` activity. The probe checks its execution count and every workflow’s terminal status. After the startup measurement, both hosts install the same in-process scheduler, activity invoker and no-op commit handler so a run exercises the composed service provider without external storage or hosted background services.

The script disables tiered JIT compilation and application logging for every measurement process. It prints the actual runtime version, architecture and GC mode for each row. Database-backed workflows, NativeAOT publication, HTTP requests and misses in the feature lookup table require separate measurements.

## Results on November 18, 2026

Measurements used an 18-core Apple M5 Pro with 48 GiB RAM, macOS 27.0.1 arm64, SDK 11.0.100 and managed .NET 11.0.0 with server GC. Each cell contains the median and [25th, 75th percentile] of ten fresh processes. [Per-process samples](samples.csv)

| Graph | Mode | Startup (ms) | Startup allocation (bytes) | Successful lookup (ns) | Completed workflows/s |
|---|---|---:|---:|---:|---:|
| Small, 7 features | Dynamic | 24.06 [23.58, 26.57] | 261,248 [261,248, 261,248] | 6.83 [6.75, 6.86] | 46,619 [46,060, 46,697] |
| Small, 7 features | Generated | 21.14 [20.32, 22.30] | 221,256 [221,256, 233,592] | 7.19 [7.09, 7.33] | 45,403 [44,423, 46,624] |
| Web server, 42 features | Dynamic | 102.39 [100.05, 110.83] | 7,835,384 [7,835,384, 7,835,384] | 6.71 [6.68, 6.81] | 23,460 [23,071, 23,862] |
| Web server, 42 features | Generated | 105.57 [101.82, 110.90] | 7,144,864 [7,144,864, 7,144,864] | 7.28 [7.17, 7.38] | 22,931 [22,355, 23,366] |

Generated composition reduced median startup allocation by 15.3% on the small graph and 8.8% on the web graph. The small graph started 2.92 ms faster. The web graph’s median startup was 3.17 ms slower, with overlapping interquartile ranges. Successful lookups took an additional 0.37 ns on the small graph and 0.57 ns on the web graph. Workflow throughput medians were 2–3% lower in Generated mode, with overlapping interquartile ranges on both graphs. This run shows no consistent workflow throughput gain from the small graph’s startup improvement.
