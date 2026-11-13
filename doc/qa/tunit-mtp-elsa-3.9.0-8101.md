# Issue #8101: TUnit/MTP release 3.9.0 validation

> Historical snapshot: provider lists, commands, and test counts below describe the assessed revision. Current support is PostgreSQL and in-memory only; removed provider packages and vNext tests are no longer available. See the [storage upgrade guide](../postgres-inmemory-migration.md).

Date: 2026-11-13 (Europe/Minsk)

## Source and isolation

- Worktree: `elsa-core-tunit-8101-3.9.0`.
- Branch: `spike/8101-tunit-mtp-elsa-3.9.0`.
- Preserved local TUnit base: `e3c72880fa933d5c2e81ff6223bdcef031c13391`.
- Original remote migration tip: `e16f5fb10c3d56f83a092fab9cbf794710feacc2`.
- Official upstream lightweight tag `3.9.0`, also its peeled commit:
  `6436609a1a3874d3fea7ccf690897792f5a1f702`.
- Merge base: `507522469ba810ee8b618ebd60294823436d6820`.

The release was fetched from `upstream` and merged with `git merge --no-commit --no-ff 3.9.0`.
The original checkout and `spike/8101-tunit-mtp` worktree keep their original branches.
The original checkout's modified Alterations project and two untracked files were not
copied into this worktree; their status and SHA-256 content hashes are checked separately.

The release changes 314 paths relative to the merge base. Text conflicts crossed central
packages, the solution, shared test support, component infrastructure, and tests. Production
release changes and public APIs were retained. Two existing differences from the tag are
preserved from the local TUnit base: the Copilot logging override at 10.0.11 (commit
`900d7dc4775b3e2d13ad2ef10c54e3e8b6a26054`, required by its existing dependency graph),
and the ElsaScript constructor-parameter tuple refactor from `48337b147`, which retains
the same selected constructor and parameter order. This merge introduces neither change.

## Conflict decisions and native boundary

- Keep TUnit/TUnit.AspNetCore 1.68.4 and root `global.json` MTP selection. The new shared
  authorization host requires TestHost; net10 TestHost stays aligned with the existing
  MVC Testing 10.0.11, while net8/net9 retain the release's framework-specific versions.
- Keep existing solution GUIDs, component host, and Mediator entry; add the new StructuredLogs
  conformance project exactly once, with Release configuration and filter routing.
- Preserve release tenant-aware bookmark processing and scoped worker registration while
  retaining awaited cancellation/disposal of the migration's test worker. Each worker captures
  its tenant, pushes that context when awaiting signals, and processes through its tenant scope.
- Keep one session `App` through `ClassDataSource<App>(Shared = SharedType.PerTestSession)`
  and inherited `NotInParallel(nameof(AppComponentTest))`. PostgreSQL ownership tests use a
  separate per-class container and their former collection's own key, without consuming App.
- Preserve release workflow-instance filtering and exact single-record assertions in input/output
  logging; remove the skip that the release removed. Preserve the release permission,
  revocation, activation, recovery, tenant-ownership, and original-source scenarios.
- Use native async assertions, typed method data, `Arguments`, inherited-test markers, and
  `Before/After(Test)` hooks. Keep exact exception type checks and ordered collection equality.
  Assertion overload review caught a nullable `Func<Task>` that needed an object-value assertion.
- Protect only real shared FastEndpoints/endpoint-security state; keep the existing subprocess
  isolation for strict ObjectConverter tests and use local/DI-owned mutable workflow state.
- Partial initialization and teardown of new authorization hosts preserve cleanup. The new
  user-cancel/drain test cancels and joins its runner before service-provider disposal; an
  unfinished runner fails cleanup, and primary/cleanup failures are aggregated.
- NUKE retains compile/package work. Both PR workflow source and generated YAML invoke native
  `dotnet test` directly. Release branch routing, 3.9.0 package version, and OIDC NuGet output
  from the release remain. No NativeAOT, global limiter, new semaphore, new sleeps, disabled
  scenario, relaxed assertion, VSTest runner, or per-project Coverlet gate was introduced.

## Inventory

| Scope | Active test applications |
| --- | ---: |
| Unit | 39 |
| Integration | 21 |
| Component | 1 |
| Total | 61 |

Five conformance projects are included within the unit/integration rows (one unit, four
integration). The performance BenchmarkDotNet executable, component host, and TLS smoke
utility are not test applications. The solution has 170 distinct project entries. The filters
contain exactly 60 unit/integration applications and one component application, matching
filesystem inventory. Coverage CI therefore expects 60 + 1 shards, with one aggregate 10%
line gate. No historical 3,974/4,417/205 case total is used as a 3.9.0 acceptance target.

Restored graphs for all 61 test applications resolve TUnit 1.68.4, MTP 2.4.0,
Microsoft coverage 18.10.0, and TRX reporter 2.3.3 (with MTP abstractions 2.4.0).
They contain no xUnit, Microsoft.NET.Test.Sdk, or Coverlet. The active source/configuration
scan likewise contains no xUnit framework/runner coupling; framework-neutral `TestAssert`
helpers and historical issue/spec prose are intentionally retained.

## Environment and commands

Local validation uses macOS 27.0.1 (26A434), arm64, SDK 10.0.400, runtime 10.0.11,
and Docker Desktop. The local TUnit reference checkout is v1.61.38, older than the pinned
1.68.4; version-specific APIs and filters were checked against package XML and the official
[v1.68.4 source/docs](https://github.com/thomhurst/TUnit/blob/v1.68.4/docs/docs/execution/test-filters.md).
The installed default SDK is 11.0.100, so local commands select the
same .NET 10 SDK family as CI explicitly:

```bash
dotnet /usr/local/share/dotnet/sdk/10.0.400/dotnet.dll restore Elsa.sln
dotnet /usr/local/share/dotnet/sdk/10.0.400/dotnet.dll build Elsa.sln --configuration Release --no-restore
TUNIT_OTEL_RECEIVER=0 dotnet /usr/local/share/dotnet/sdk/10.0.400/dotnet.dll test \
  --project <project.csproj> --configuration Release --no-build \
  --results-directory <unique-results-directory> -- \
  --report-trx --timeout 10m --progress off --ansi off --show-test-results none
```

`TUNIT_OTEL_RECEIVER=0` disables the optional runner OTLP receiver only; it does not alter
Elsa telemetry or scheduling. Restore was repeated outside the sandbox for NuGet/cache
access. Final Release build passed with zero errors; warnings include pre-existing nullable,
analyzer, and NuGet audit diagnostics. No audit warning was suppressed or unrelated package
upgraded. The shared testing library also built all three target frameworks successfully.

## Targeted verification

All 20 release-changed unit/integration projects passed: **2,548 passed, zero failed/skipped**.
Scoped source comparisons preserved test method identities, inline arguments, typed data-source
expansion, exact exception/cardinality checks, and collection order. The workflow-unit tranche
alone preserves 318 methods and 63 inline rows across 53 release files; integration preserves
102 methods and 16 inline rows. Inherited provider suites are verified as leaf outcomes in
runner reports rather than counted solely from source attributes.

| Changed project | Passed cases |
| --- | ---: |
| `Elsa.AI.Host.UnitTests` | 52 |
| `Elsa.AI.IntegrationTests` | 69 |
| `Elsa.Activities.IntegrationTests` | 125 |
| `Elsa.Api.Common.UnitTests` | 108 |
| `Elsa.Dashboard.Api.UnitTests` | 114 |
| `Elsa.Dsl.ElsaScript.IntegrationTests` | 24 |
| `Elsa.Expressions.UnitTests` | 23 |
| `Elsa.ExternalAuthentication.IntegrationTests` | 181 |
| `Elsa.Identity.UnitTests` | 246 |
| `Elsa.Persistence.EFCore.UnitTests` | 93 |
| `Elsa.Resilience.IntegrationTests` | 14 |
| `Elsa.Scheduling.UnitTests` | 62 |
| `Elsa.UserTasks.UnitTests` | 110 |
| `Elsa.Workflows.Api.UnitTests` | 75 |
| `Elsa.Workflows.Core.UnitTests` | 305 |
| `Elsa.Workflows.IntegrationTests` | 326 |
| `Elsa.Workflows.Management.UnitTests` | 129 |
| `Elsa.Workflows.Persistence.ConformanceTests` | 35 |
| `Elsa.Workflows.Runtime.UnitTests` | 435 |
| `Elsa.Diagnostics.StructuredLogs.Persistence.ConformanceTests` | 22 |

## Component lifecycle and coverage

The full component coverage run passed **222 outcomes: 220 passed, two skipped, zero failed**.
JSON and TRX agree, with 222 unique case IDs. PostgreSQL ownership contributes 17/17 passing
cases. Input/output logging passes all four cases, including the release-reenabled scenario.
The two skips retain their reasons:

- `DeleteWorkflow_Clustered`: `Clustered tests are interfering with other event driven tests`.
- `ImportWorkflowActivity_ShouldUpdateOtherPods`: `Not yet implemented`.

The report/source audit covers all 50 concrete App consumers, including four inheriting through
`TimerActivityTestBase`. Their 198 outcomes comprise 196 passed and the two skips. Each of the
196 executed cases contains exactly one setup hook and one cleanup hook inside its complete
root span. An interval sweep of those spans gives peak App concurrency **one**. There is one
`initialize App` span and one `initialize Infrastructure` span. PostgreSQL has no App hooks;
its fixture initializes once, and the seven other independent outcomes retain useful parallelism.

The current package command was exercised with `Elsa.Component.Tests.slnf`, `--no-build`,
`--coverage --coverage-settings <repo>/test/coverage.settings.xml`,
`--coverage-output-format cobertura --report-trx --hangdump --hangdump-timeout 5m
--hangdump-type Mini`. It emitted one fresh Cobertura shard. Native duration 87,403.756 ms
is a coverage-run observation, not a matched framework performance measurement.

## Full-run race diagnosis

The first full solution attempt completed 60 applications but stalled in one runtime case:
`BookmarkQueueAmbientTenantTests.RecurringTask_ExecuteAsync_SignalsCapturedTenant_WhenAmbientTenantIsMissing`.
Comparing the successful 435-case targeted JSON with the still-open native TRX stream found
434 completed IDs and exactly this missing case. A managed stack snapshot showed asynchronous
waiting; source inspection identified two readers competing for one signal: the real worker
and the test's additional channel reader. This race existed in the release test and was exposed
by full-suite scheduling.

The test now observes the tenant inside the real `IBookmarkQueueProcessor.ProcessAsync` through
a test-local completion source, with a five-second bounded wait and `StopAsync` in `finally`.
It retains the real recurring task, signaler, worker, missing-ambient assertion, and expected
`tenant-a` assertion. No production behavior, serialization policy, or count was changed.
The stalled attempt was explicitly cancelled and is not claimed as a passing full run.

The second full attempt completed all 61 applications and established the current discovery
inventory: 4,937 total, with 146 intentional skips. It had two failures (4,789 passed), which
were investigated before accepting any full run:

- The deadline scenario raced its 500 ms natural activity timer against the 50 ms drain
  deadline under process load. The test activity now remains active until the real drain
  cancels its real execution-cycle token, using an invocation-local completion source and
  cancellation registration. The cycle token is distinct from the activity caller token; the
  helper also observes caller cancellation for teardown.
  Release terminal-state, cancellation-count, no-requeue, and forensic-log assertions stay intact.
  The separate user-cancel workflow keeps its original duration-based behavior.
- The SQLite conformance suite's new disabled-tenancy scenario could seed EF's shared model
  cache without tenant filters. Only `tenantsEnabled:false` now bypasses service-provider
  caching, separating that model shape from the enabled cases. Normal caching/parallelism,
  exact visible-record counts, and tenant-agnostic assertions are retained. This test-fixture
  boundary follows the existing Alterations conformance pattern and the
  [EF Core 10.0.9 cache implementation](https://github.com/dotnet/efcore/blob/v10.0.9/src/EFCore/Internal/ServiceProviderCache.cs).

## Repeat-run validation

- PostgreSQL ownership: ten sequential sessions, each 17 passed, zero failed/skipped.
- Shared-App input/output logging plus multitenancy: ten sequential sessions, each 11 passed,
  zero failed/skipped. Exact native OR filter:
  `/*/*/(InputOutputLoggingTests)|(MultitenancyTests)/*`.

The selected identities and counts were verified in each finalized JSON/TRX report. The
initial combined whole-path filter selected only the PostgreSQL cohort; those runs are not
claimed as App validation. A separate broad-filter diagnostic run passed the complete
222/220/2 component set. The final class-segment OR expression above selects precisely the
11 App cases. No filter, limiter, or additional count gate was added to repository CI.

These commands intentionally add no minimum-count option. Report totals and executed cases
are checked separately; for example the 222 component outcomes contain only 220 executions.
Skips must not be included in an application-level MTP `minimum-expected-tests` floor.

- User-cancel/drain and deadline recovery cleanup: ten sessions of two cases, with runner
  completion required before service-provider disposal. The class-segment filter is
  `/*/*/(UserCancelDuringDrainTests)|(DrainRecoveryEndToEndTests)/*`.

- Installed-features authorization host: ten sessions, each four passed, zero failed/skipped,
  using `/*/*/InstalledFeaturesAuthorizationTests/*`, after moving host creation into a native
  setup hook and making partial teardown null-safe.

- Fixed runtime project: ten complete sessions, each 435 passed, zero failed/skipped,
  with unchanged native parallelism (4,350 passing executions).

- Workflow integration after causal deadline coordination: ten complete sessions, each
  326 passed (3,260 executions), zero failed/skipped.
- Workflow persistence conformance after model-cache isolation: ten fresh complete sessions,
  each 35 passed (350 executions), zero failed/skipped, including both enabled/disabled tenancy.

## Full solution and coverage verification

Full solution, complete coverage aggregation, and final commit/ancestry checks are recorded
below when finished. Remote GitHub Actions and IDE interaction are not
part of local execution; no remote workflow result or controlled speedup is claimed.
