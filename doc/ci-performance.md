# CI performance plan

Research date: 2026-11-14. Base: `origin/main` at `245760a22`.

## Current validation workflow

The Packages workflow builds and uploads validation artifacts on `main` pushes
and manual runs. Development branches use PR validation; run Packages manually
on a branch when coverage or package artifacts are needed. The full-solution
package build runs alongside the unit/integration
and component suites on both `net10.0` and `net11.0`. The aggregate report still
requires both suites to pass and enforces 10% line coverage. A successful workflow
requires the build when enabled, all test lanes, and coverage reporting to succeed; package
artifacts from a failed workflow do not establish a validated release.

Download `elsa-nuget-packages`, the framework-specific test-result artifacts, and
`coverage-reports` from the Actions run. Manual Packages runs default to the same
artifacts as push runs. Set `build_packages` to false for coverage validation
without the separate build/pack job; all four test lanes and the coverage gate
still run. An empty package upload fails the build job when enabled. Superseded
automatic Packages and Docker runs are canceled; explicit manual requests stay
independent. The PR and Docker certificate workflows can also be run manually, preserving their
existing framework and image matrices.

The inherited Feedz/NuGet publishers, release-event handling, and GitHub Pages
deployment have been removed. Validation needs no publishing secrets, NuGet OIDC
policy, or Pages site. All validation workflows request only `contents: read`;
the separate stale workflow requests issue and pull-request write access for
its maintenance job. Weekly Dependabot updates cover npm and GitHub Actions.
Routine NuGet version scans are disabled to stop Feedz access in the shared
restore graph; builds retain their required Feedz sources and package mappings.
See the [workflow run audit](ci-workflow-runs.md) for the hosted baseline and
the reasoning behind the trigger and optional-package policy.

PRs changing only `design/video/elsa-readme-video/**` skip the .NET matrix while
retaining the `ubuntu-latest` check. A lightweight job compares the synthetic
merge with its first parent using a depth-two checkout, NUL-separated paths,
and rename detection disabled so moves include deleted source paths. Mixed,
unknown, empty, and production ClientLib diffs run the full matrix. Failed
classification fails the result gate; manual runs always validate .NET.
This decision does not establish that the video's npm build succeeds.

The sections below record the performance work that established this validation
graph. References to publishing in those historical findings predate the removal
of inherited release automation.

## Success criteria

- Preserve both `net10.0` and `net11.0` correctness tests, full-solution build validation,
  aggregate coverage threshold, failure artifacts, and validation gates.
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
4. Evaluate and adopt dependency locks where appropriate, then review the changes
   independently, resolve findings, and record validation and remaining measurements.
   Keep each implementation step in a conventional commit.

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
| Dependency locks | Adopt committed locks for executable/test entry points after validating the actual graph. They pin resolved transitives and package hashes; CPM alone does not do that. Keep explicit cache keys including locks and tool/restore configuration. |
| `setup-dotnet` built-in cache | Applicable once locks exist. Keep the explicit shared cache action to give different solution filters and tool restores their own primary keys and controlled fallbacks. |
| Framework-level PR parallelism | Both frameworks currently share two CPU-based MTP slots. Independent runners can shorten feedback time, with additional runner/build cost. Preserve full-solution compilation, including net10-only benchmarks. |
| Main package build overlap | Build packages alongside tests; successful validation still requires the aggregate coverage gate. This saves the serialized build interval on successful runs but also builds artifacts for commits whose tests fail. |
| Framework-specific test restore | Initial optimization rejected after the lock-file experiment: a two-target lock fails validation with a single-target restore. Restore the complete locked graph, then select the test framework with `--framework`. |
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

## Lock-file applicability

The solution contains 135 projects; two further project files sit outside it.
CPM defines framework-dependent Microsoft package versions, disables transitive
pinning, and uses no explicit floating versions or version ranges. Preview
dependencies are versioned explicitly, including packages from mapped Feedz feeds.
None of this prevents NuGet lock files: the copied real solution restored and
generated its complete multi-target graph successfully.

Generating a lock for every solution project produces 135 files, about 9.4 MB
and 234,269 lines. Instead, enable locks only for `OutputType=Exe`: 64 executable,
test, and benchmark entry points in the solution, plus the standalone TLS smoke
application. These entry-point locks record project-reference dependencies and
their resolved packages. A reusable library's own lock would not constrain the
dependency graph chosen by applications consuming its package.

Actual SDK 11.0.100 experiments on the copied repository established:

- Full-target locked restore succeeds with CPM, project references, preview feeds,
  and a CI `Version` override used for packaging.
- Changing a transitive library dependency fails an entry-point locked restore
  with `NU1004`, as desired.
- NuGet otherwise creates an absent lock even in locked mode. The repository's
  `ELSA0003` guard rejects a missing executable lock during locked restore;
  ordinary unlocked restore can generate it for review.
- Restoring with `-p:TargetFramework=net11.0 --locked-mode` against complete
  `net10.0;net11.0` locks fails with `NU1004`. CI must restore both frameworks;
  test execution remains framework-specific.
- Three warm-cache UnitIntegration restores, with clean `obj` each time and
  NuGet audit disabled for the timing comparison, had medians of 3.991s for
  single-target unlocked restore and 6.012s for complete locked restore on macOS
  ARM64. This approximately two-second cost buys graph validation; it is not a
  hosted-runner performance prediction.

Update dependencies locally with a complete
`dotnet restore Elsa.slnx --force-evaluate`, review the changed locks, and commit
them with package changes.
For the standalone smoke app, restore `test/TlsSmoke/TlsSmoke.csproj` separately.
CI uses `--locked-mode`; a package cache hit never skips restore. SDK patch
roll-forward remains enabled, so restore changes introduced by a newer SDK may
require reviewed lock updates. Existing NuGet audit behavior remains enabled.
For a focused test, first restore its complete project graph, then use
`dotnet test --project <path> --framework net11.0 --no-restore` (or `net10.0`).
Do not regenerate committed two-target locks with a single-target restore.

The tools workload only falls back to other tools caches. Downloading a complete
solution dependency cache into a report-tool job can exceed the cost of restoring
the tool itself (the historical tool restore took 6s, while package-cache restores
took 15–18s). Solution and test workloads retain broader package reuse.

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
- [NuGet dependency locks and library guidance](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies)

## Measurement after rollout

Compare at least three successful PR runs and three main runs with cold and warm
caches, on the same runner class. Record setup/cache restore/save, NuGet restore,
build, tests per framework, report, package build, total elapsed time, and summed
runner minutes. Check all expected modules and coverage files remain present.
Local timings cannot establish a hosted Linux speedup; no percentage is claimed.

## Validation and review

Validated locally with SDK 11.0.100 on macOS ARM64 and Docker:

- Complete versioned locked restore:
  `dotnet restore Elsa.slnx --locked-mode -p:Version=3.9.0-preview.999`.
- Release build of `Elsa.slnx` for all targets: successful, zero errors. Existing
  nullable and analyzer warnings remain; the three nullable matcher warnings
  identified during migration were corrected and their project rebuilt cleanly.
- Complete MTP run of `Elsa.slnx`, Release, `--no-build`, two parallel modules:
  **9,194 total; 9,190 passed; four existing skips; zero failures**.
- Additional Runtime/Core reruns after restoring exception type assertions:
  1,480 passing executions across both frameworks. Resilience rerun: 192 passed,
  with zero build warnings/errors.
- Coverage probe with two test projects on net11: 20 tests passed and two raw
  Cobertura files remained at the result root. SDK-generated merged artifacts
  reside in a subdirectory, preserving the workflow's raw report-count checks.
- actionlint 1.7.12 and `git diff --check`: passed. Lock generation, missing-lock
  failure, unlocked regeneration, transitive drift rejection, and package Version
  overrides were exercised against the actual restore graph.

Independent review followed the first four conventional commits. It found nine
exception assertions whose generic type arguments had been lost during mechanical
mock conversion. The original assertions were restored verbatim. An AST comparison
confirmed all 1,717 original direct assertions in the affected three workflow test
projects remain; logging verifications retain counts and message predicates.
Review also identified the need to keep tools-cache fallbacks separate from large
solution caches. These corrections were checked again. No unresolved actionable
review findings remain.

Hosted Linux workflows, remote cache transfer costs, and package publication were
not executed. The branch remains local; the rollout comparison above is still
needed before claiming a CI speedup.

The later publication-preparation workflow changes passed actionlint 1.7.12 and
`git diff --check`. Structural comparison against `baeef4331` confirmed unchanged
test jobs, framework matrices, raw coverage-count checks, aggregate coverage gate,
and restore/build/pack commands. It also checked manual package artifact uploads,
removed publishers/Pages, token permissions, and both Dependabot ecosystems.
These workflow changes have not been executed on a hosted runner.
