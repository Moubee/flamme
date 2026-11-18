# Published Generated composition on managed .NET and NativeAOT

The same .NET 11 executable selects the seven-feature `WorkflowsFeature` graph at build time in both publications. Each process verifies its runtime’s dynamic-code support, the concrete `FixedFeatureModule`, and the presence of `WorkflowsFeature`. The lookup and workflow samples report the installed feature names. All measured processes used server GC.

## Run

Use the repository’s SDK and a host with a native C toolchain. Choose `osx-arm64` on an ARM64 Mac or `linux-x64` on a Linux x64 machine. Both RIDs have native and managed NuGet locks. The measurements below used macOS.

```sh
rid=osx-arm64
workload=test/performance/feature-composition/native/Workload/Workload.csproj
launcher=test/performance/feature-composition/native/Launcher/Launcher.csproj
export ELSA_PUBLISHED_PROBES="$(mktemp -d)"
export ELSA_PUBLISHED_SAMPLES="$ELSA_PUBLISHED_PROBES/stages.csv"
export DOTNET_TieredCompilation=0
export Logging__LogLevel__Default=None

for mode in managed native; do
  dotnet publish "$workload" -c Release -r "$rid" --self-contained \
    -p:NativeAotMode="$mode" -p:RestoreLockedMode=true -o "$ELSA_PUBLISHED_PROBES/$mode"
done
dotnet run --project "$launcher" -c Release -p:RestoreLockedMode=true -- --filter '*PublishedGraph*'
```

The .NET 10 BenchmarkDotNet launcher starts a fresh published .NET 11 workload process per iteration, following TUnit’s published-executable comparison. The SDK’s `PublishAot` property is scoped to the native workload project. BenchmarkDotNet measures the time to launch and wait for each process. The child records stage times separately, and the launcher saves the output during iteration cleanup. [TUnit runtime benchmarks](https://github.com/thomhurst/TUnit/blob/main/tools/speed-comparison/Tests.Benchmark/RuntimeBenchmarks.cs)

## Measurements

November 18, 2026, Apple M5 Pro, 18 cores, macOS 27.0.1 arm64, SDK 11.0.100, .NET 11.0.0, BenchmarkDotNet 0.15.8. Each cell gives the median [25th, 75th percentile] of ten fresh processes. Quartiles use inclusive linear interpolation. The sixty per-iteration records include BenchmarkDotNet’s process duration and the child’s corresponding stage measurements. [Samples](samples.csv)

| Workload | Self-contained managed | NativeAOT |
|---|---:|---:|
| Process launch through first Apply and exit (ms) | 83.89 [82.26, 85.65] | 16.95 [16.07, 18.31] |
| First Apply (ms) | 20.29 [20.14, 20.71] | 1.82 [1.79, 2.03] |
| Managed bytes allocated through first Apply | 238,920 [232,728, 238,920] | 852,384 [852,384, 852,416] |
| Successful feature lookup (ns) | 7.26 [7.06, 7.31] | 6.27 [6.23, 6.29] |
| Completed Inline workflows/s | 46,325 [45,910, 47,245] | 16,258 [15,893, 16,535] |

First Apply runs from just before `CreateModule` through `Use<WorkflowsFeature>()` and the first `Apply()`. Service collection setup precedes this interval. `GC.GetTotalAllocatedBytes(true)` measures process-wide managed allocations. The two publications installed the same seven features. The lookup benchmark warms up 100,000 successful calls, then measures ten million calls to `HasFeature<WorkflowsFeature>()`.

The workflow case builds one Workflow → Inline graph from the composed service provider and asserts it contains both nodes. It runs 1,024 warmups before timing 4,096 sequential executions with the default in-process scheduler and invoker and a no-op commit handler. It checks each workflow’s terminal status and counts each activity execution. This closed graph explicitly roots the `Composite.Root` property inspected by the activity-port resolver. Initial native runs exposed the constructor requirements on behavior and storage-driver registrations and the reflected public methods on workflow, activity and mediator middleware.

On this workload, NativeAOT shortened the median first Apply by 18.48 ms and used 613,464 additional managed bytes during that stage. Its warmed lookup was approximately 1 ns faster. The managed process completed about 2.85 times as many workflows per second.

## Full reference web server

A separate osx-arm64 publication of the actual `Elsa.Server.Web` project produced a 61,475,024-byte (58.63 MiB) ARM64 Mach-O executable. On launch, `WorkflowsApiFeature.Apply()` threw `InvalidOperationException: FastEndpoints was unable to find any endpoint declarations!` The process exited via SIGABRT before HTTP startup. The seven-feature results above cover the completed workload. The full reference server has no native startup or HTTP throughput measurement.

The full-server check and both workload publications used the schema 4 generator, runtime, and reference server composition in [the benchmark commit](https://github.com/Moubee/flamme/commit/92a32851a4fad3471165ef056d1632921df25c13). To repeat the web check, set `<PublishAot>true</PublishAot>` in the web project’s property group in a scratch checkout. This confines the native setting to the executable and lets the compiler tools build for .NET Standard.

```sh
dotnet publish src/apps/Elsa.Server.Web/Elsa.Server.Web.csproj -c Release -r osx-arm64 \
  -p:RestoreLockedMode=false -p:RunAnalyzers=true -o /tmp/flamme-native-web
(cd /tmp/flamme-native-web && DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://127.0.0.1:0 ./Elsa.Server.Web)
```
