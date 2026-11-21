# Activity descriptor and runtime binding benchmarks

These BenchmarkDotNet fixtures follow an activity from compilation to execution. Run them from the repository root with the SDK in `global.json` and the .NET 10 runtime.

| Fixture | Work measured |
| --- | --- |
| `CompileBenchmarks` | Parse and emit the same four-activity producer with warm Roslyn metadata references. On revisions that contain the activity generator, run it with a fresh driver before emit. Setup checks the emitted types and prints source, generated code and DLL sizes. |
| `StartupBenchmarks` | Launch a fresh .NET process, construct its first `If`, verify its type and version, and wait for exit. The probe references Flamme Core. Allocation results for this case cover the parent process only. |
| `CatalogBenchmarks` | Register activities from the Core assembly, populate a fresh registry, refresh an existing one, and look up a cached descriptor. Setup logs the activity counts and an ordered catalog fingerprint. |
| `RuntimeBenchmarks` | Construct a warm `If`, read inputs and outputs, traverse child ports, bind a private completion callback, and build and run an `If → SetName` workflow. |
| `WorkflowBindingBenchmarks` | Materialize an omitted input, record the first renamed output, and suspend and resume a workflow by bookmark ID. The workflows reuse a DI scope and run in memory with a no-op state commit. |

## Run the suite

```sh
project=test/performance/Flamme.Workflows.PerformanceTests/Flamme.Workflows.PerformanceTests.csproj
dotnet run -c Release --project "$project" -- --filter '*ActivityDescriptors*' --job Dry
```

The dry job validates all 15 cases. For a timing run, choose a method or fixture with `--filter`. This example gives the compiler time to warm up and writes full JSON and Markdown reports.

```sh
dotnet run -c Release --project "$project" -- --filter '*ActivityDescriptors.CompileBenchmarks*' \
  --launchCount 1 --warmupCount 45 --iterationTime 400 --iterationCount 10 --artifacts ./BenchmarkDotNet.Artifacts/compile
```

## Compare two revisions

To compare with the pre-generator implementation, run this from the benchmark branch with a fresh sibling worktree. The overlay supplies the same fixtures and runner references to the baseline. Its own Flamme code and compiler determine which paths run.

```sh
suite=$(git rev-parse HEAD)
baseline=../flamme-descriptor-benchmark-main
git worktree add --detach "$baseline" 62b59b990079b9f5e833c8319b82e181c8f8257b
git -C "$baseline" restore --source="$suite" --worktree -- \
  test/performance/Flamme.ActivityStartupProbe \
  test/performance/Flamme.Workflows.PerformanceTests/ActivityDescriptors \
  "$project"
(cd "$baseline" && dotnet restore "$project" --force-evaluate)
```

Run the same command and job settings on both worktrees, using a distinct artifacts directory for each launch. For two measurements per revision, use baseline → candidate → candidate → baseline. Check the catalog counts, fingerprint, producer source hash and compiler version in the logs before comparing means or managed allocations. Use a quiet machine for timing runs.

The original comparison includes both launch means, allocation results and the exact job settings used for each case. [PR #122 benchmark results](https://github.com/Moubee/flamme/pull/122#issuecomment-7066708668)
