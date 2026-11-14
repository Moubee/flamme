# CI performance plan

Research date: 2026-11-14. Base: `origin/main` at `245760a22`.

## Success criteria

- Preserve both `net10.0` and `net11.0` correctness tests, full-solution build validation,
  aggregate coverage threshold, failure artifacts, and publishing gates.
- Reduce repeated dependency downloads and shorten the critical path without caching
  compiled outputs or bypassing restore on a cache hit.
- Replace NSubstitute with the TUnit mocking alternative, preserving test behavior.
- Validate the commands with SDK `11.0.100`, lint workflow changes, and review after
  three or four conventional commits. Report unverified hosted-runner behavior explicitly.

## Decomposition

1. Audit the current workflow graph, collect available timings, and research caching,
   managed TUnit/MTP, Native AOT, and current SDK capabilities (this document).
2. Add consistent dependency caching and reduce workflow serialization and redundant work.
3. Migrate existing mocks to `TUnit.Mocks`, remove legacy package references, and run
   affected tests on both supported frameworks.
4. Review the changes independently, resolve findings, and record validation and remaining
   measurements. Keep each implementation step in a conventional commit.

## Baseline

[PR run 43568320384](https://github.com/DenDeline/flamme/actions/runs/43568320384/job/132366948160)
completed in 17m44s: runner cleanup 55s, checkout 4s, SDK setup 1s, restore 51s,
build 5m57s, and tests 9m51s. Its 117 test modules share two execution slots;
both frameworks compete for the same slots. Component tests take about 111s per
framework. This is one observation, not a statistical baseline.

The PR workflow restores and builds the whole solution, then runs both frameworks
without rebuilding. It has no NuGet cache. Main's Packages workflow has two
test-suite jobs, each with a two-framework matrix, followed by an aggregate report,
then a fresh full-solution package build. Only test jobs cache NuGet packages;
their key omits SDK and restore configuration and is shared by different subsets.
The package build and tool restore have no dependency cache.

No successful post-upgrade main package run was available during this audit.
Older failed runs are unsuitable for claiming a current-main speedup.

## Decisions and tradeoffs

| Option | Assessment |
| --- | --- |
| Dependency cache | Adopt for restore consumers. Key by OS, architecture, restore inputs, and workload/framework; broader fallback caches still require restore. Separate workloads avoid an immutable cache being permanently populated by the first, smallest matrix subset. |
| `setup-dotnet` built-in cache | The documented contract uses `packages.lock.json`; this repository has no lock files. Use explicit `actions/cache` rather than introducing a lock-file migration into the performance change. |
| Framework-level PR parallelism | Both frameworks currently share two CPU-based MTP slots. Independent runners can shorten feedback time, with additional runner/build cost. Preserve full-solution compilation, including net10-only benchmarks. |
| Main package build overlap | Build packages alongside tests; gate every publisher on successful aggregate coverage. This saves the serialized build interval on successful runs but also builds artifacts for commits whose tests fail. |
| Framework-specific test restore | Restore the matrix framework with `-p:TargetFramework=...`, then use the same `--framework` for tests. Do not constrain the multi-targeted package build. |
| Cache `bin` / `obj` | Defer: Fody, generated code, framework selection, paths, and restore state complicate correctness. |
| Increase MTP parallelism | Defer until CPU, memory, and container pressure are measured. MTP already defaults to processor count; more processes are not free capacity. |
| Native AOT | Defer whole-suite adoption. TUnit supports it, but runtime scripting and dependency compatibility need separate work. Compare restore + publish + instrumentation + execution, not execution time alone. |
| Affected-test selection | Defer experimental selection; retain complete correctness gates. |

TUnit's generated discovery and invocation are already enabled in normal managed
builds. Native publishing does not activate that benefit. NSubstitute uses runtime
proxies and is incompatible with Native AOT; the requested TUnit mock migration
removes that dependency, but `CSharpEvaluator` still compiles Roslyn scripts at
runtime and other dynamic code remains. AOT coverage exists through a separate
instrumentation path; it is not a drop-in replacement for the current managed
coverage commands.

The repository already selects the .NET 11 GA SDK, with its current CLI and
MSBuild optimizations. Keep SDK selection in `global.json` and retain the .NET 10
runtime required by the second test target.

## Sources

- [TUnit engine modes](https://tunit.dev/docs/execution/engine-modes/)
- [TUnit migration from NSubstitute](https://tunit.dev/docs/migration/mocking/nsubstitute/)
- [TUnit benchmark methodology](https://tunit.dev/docs/benchmarks/methodology/)
- [Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [EF Core Native AOT support](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)
- [AOT coverage instrumentation sample](https://github.com/microsoft/codecoverage/blob/main/samples/Algorithms/scenarios/scenario05/README.md)
- [.NET 11 SDK capabilities](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk)
- [MTP CLI and concurrency](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- [`setup-dotnet` cache contract](https://github.com/actions/setup-dotnet#caching-nuget-packages)
- [`actions/cache` keys and fallback](https://github.com/actions/cache)

## Measurement after rollout

Compare at least three successful PR runs and three main runs with cold and warm
caches, on the same runner class. Record setup/cache restore/save, NuGet restore,
build, tests per framework, report, package build, total elapsed time, and summed
runner minutes. Check all expected modules and coverage files remain present.
Local timings cannot establish a hosted Linux speedup; no percentage is claimed.
