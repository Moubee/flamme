# Migrate the Elsa clock abstraction to TimeProvider

## Status

Accepted for the breaking TimeProvider release. The migration replaces public
clock APIs and registrations, adopts FakeTimeProvider in tests, and preserves
persisted timestamp formats and existing timer mechanisms. Release validation
follows the gates below. The temporary adapter described here is removed from
the completed implementation.

## Context and research

The baseline is `origin/main` at `239ae5b99`. Flamme has its own
[`Elsa.Common.ISystemClock`](https://github.com/Moubee/flamme/blob/239ae5b99/src/modules/Elsa.Common/Contracts/ISystemClock.cs),
with one member: `DateTimeOffset UtcNow`. Its
[`SystemClock`](https://github.com/Moubee/flamme/blob/239ae5b99/src/modules/Elsa.Common/Services/SystemClock.cs) returns
`DateTimeOffset.UtcNow`, without rounding or truncation. This is **not**
`Microsoft.AspNetCore.Authentication.ISystemClock`: Microsoft's authentication
[migration notice](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/8/isystemclock-obsolete)
describes a truncated clock that is not the implementation being replaced here.

[`TimeProvider`](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
is part of the framework on both supported targets, .NET 10 and .NET 11. Its
`GetUtcNow()` returns `DateTimeOffset`. No production compatibility package is
needed. It also provides elapsed-time measurement and timers, which have different
semantics from reading a wall clock and need separate migration decisions.

The repository already uses `TimeProvider` in
[`SimulateResponseSessionStore`](../../src/modules/Elsa.Resilience/Endpoints/SimulateResponse/SimulateResponseSessionStore.cs),
registered by
[`ResilienceFeature`](../../src/modules/Elsa.Resilience/Features/ResilienceFeature.cs).
The migration must give old and new consumers one effective UTC source during
the transition, including hosts and tests that override their clock.

### Scope and dependency map

| Area | Existing surface | Migration concern |
| --- | --- | --- |
| Common and composition | `SystemClockFeature`, `SystemClock`, `ISystemClock`, recurring schedules | Default registration, custom overrides, singleton lifetime and adapter direction. |
| Workflow core | `WorkflowExecutionContext.SystemClock`, `ActivityExecutionContext` constructor, context partials and extensions | Public API compatibility and consistent timestamps throughout an execution. |
| Runtime and management | Bookmarks, journals, state, background work, definitions and execution records | Persisted UTC instants and recovery behavior. |
| Scheduling | Cron parsing, StartAt/Delay/Timer activities, scheduled task constructors | Separate due-time calculations from timer execution, cancellation and disposal. |
| Hosting, HTTP and security | Drain deadlines, HTTP caching/timeouts, identity, SAS, secrets, external authentication | Boundary comparisons, token formats, mixed real/fake timers and UTC conversion. |
| Other consumers | Alterations, labels, user tasks, JavaScript helpers, expression activity `Now` | Preserve externally observable timestamp types and expression behavior. |
| Test infrastructure | `ActivityTestFixture`, mocked clocks and direct constructors | Explicit starting instants, clock override order and isolation between tests. |

Reproduce discovery with symbol searches, then inspect callers and partial types;
an interface-only replacement misses `context.SystemClock.UtcNow` reads:

```bash
rg -n '\b(ISystemClock|SystemClock|SystemClockFeature|TimeProvider)\b' src test
rg -n 'DateTime(Offset)?\.(UtcNow|Now)|Task\.Delay|CancelAfter|PeriodicTimer|System\.Timers' src
```

Direct wall clocks and real timers are adjacent concerns, not automatically in
scope for the first migration. In particular,
[`CronSchedule`](../../src/modules/Elsa.Common/RecurringTasks/CronSchedule.cs)
uses `UtcNow.DateTime` and reads twice, while
[`CronosCronParser`](../../src/modules/Elsa.Scheduling/Services/CronosCronParser.cs)
uses `DateTimeOffset` with an explicit UTC time zone. Characterize that difference
before choosing a correction; do not hide it in a property-to-method replacement.

### High-level decomposition

1. Establish a single default provider and a temporary adapter for unmigrated
   consumers; define the breaking changes to registrations and public APIs.
2. Establish deterministic test-time composition, then migrate UTC reads in small
   module slices with unchanged values, conversions and boundary predicates.
3. Migrate the workflow core and persistence-facing consumers with recovery and
   PostgreSQL validation; move scheduling calculations independently of timers.
4. Remove the temporary adapter and legacy APIs in the breaking release.
   Consider timer virtualization and remaining direct wall clocks in subsequent
   changes with their own concurrency and shutdown contracts.

Each implementation slice must build and pass its targeted tests independently.
Review the cumulative diff after every three or four slices, including public
APIs, clock composition, timestamp semantics and remaining legacy usage.

### Toolchain

Use the repository's [`global.json`](../../global.json): .NET SDK 11.0.100,
latest patch roll-forward, and Microsoft Testing Platform. Retain `Elsa.slnx`,
TUnit, central package management, both `net10.0`/`net11.0` targets, runtime async
on .NET 11 and the existing Fody policy. The SDK already supports `TimeProvider`;
an SDK upgrade or solution conversion is not part of this migration.

[`FakeTimeProvider`](https://learn.microsoft.com/en-us/dotnet/core/extensions/timeprovider-testing)
comes from the test-only `Microsoft.Extensions.TimeProvider.Testing` package.
Use stable [version 11.0.0](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/11.0.0),
which includes both target frameworks, through `Directory.Packages.props`.
Add versionless references in `Elsa.Testing.Shared` for the fixture and centrally
in `test/Directory.Build.props` for correctness test projects. Do not add it to production modules or
hosts. Regenerate all executable dependency locks according to
[contributor guidance](../../CONTRIBUTING.md#dependency-updates). Adding this
dependency and replacing legacy clock mocks are part of the implementation.

## Decision

Use `System.TimeProvider` directly for injected time. Ship a coordinated breaking
package update after all slices are complete. Intermediate commits are buildable
implementation stages, not supported mixed-version release configurations. Do
not retain deprecated clock interfaces, parallel public constructors, or public
`Clock` aliases in the final API.

### Composition and the temporary adapter

Introduce `TimeProviderFeature` in `Elsa.Common`. Register the default with
`Services.TryAddSingleton<TimeProvider>(TimeProvider.System)`. A host may register
one singleton `TimeProvider` before or after `AddElsa`, before building the service
provider. An explicit registration wins over the default; Elsa feature ordering
must not overwrite it. Multiple intentional host registrations follow normal DI
last-registration-wins resolution, but are discouraged. Providers must be safe
for concurrent use; a scoped provider is invalid for singleton consumers.

During implementation only, retain `SystemClockFeature` as a dependency of
unmigrated modules and make it depend on `TimeProviderFeature`. Its default legacy
registration forwards to the canonical provider:

```csharp
Services.TryAddSingleton<ISystemClock>(sp =>
    new SystemClock(sp.GetRequiredService<TimeProvider>()));
```

Temporarily give `SystemClock` a provider-taking constructor and implement
`UtcNow => timeProvider.GetUtcNow()`; its parameterless constructor delegates to
`TimeProvider.System` until removal. That compatibility constructor is confined
to the temporary adapter. Change production consumers' constructors directly to
`TimeProvider`, updating their callers and factories in the same slice. Do not
add same-arity `ISystemClock` and `TimeProvider` overloads: they complicate ordinary
DI constructor selection even with `ActivatorUtilitiesConstructor` annotations.

The dependency direction is `ISystemClock -> TimeProvider` only. Never register
a provider that resolves `ISystemClock` back from DI. Move all repository service
collection overrides of `ISystemClock` to the provider before migrating consumers
that share those containers. Legacy-only custom registrations are not supported
by the completed release; downstream hosts must update them. The temporary
adapter is not an automatic bridge for legacy overrides.

Keep Resilience's existing `TryAddSingleton<TimeProvider>(TimeProvider.System)`
fallback while it supports independent composition; it is idempotent with the
Common registration. Test Resilience alone and combined with the new feature in
both orders, with and without a host-supplied provider.

### Public API changes

| Existing API | Final API / upgrade action |
| --- | --- |
| `ISystemClock.UtcNow` | Inject `TimeProvider` and call `GetUtcNow()`. Remove the interface and its implementation. |
| `SystemClockFeature` and feature dependency attributes | Use `TimeProviderFeature`, including all six existing module dependencies. |
| `new SystemClock()` | Use `TimeProvider.System` at explicit composition boundaries, or inject a provider into application services. |
| Constructors taking `ISystemClock` | Replace the parameter type with `TimeProvider`; name it `timeProvider` and update direct calls, named arguments, factories and generated mocks. |
| `WorkflowExecutionContext.SystemClock` | Expose `TimeProvider TimeProvider`; change context partials, extensions and activity callers together. |
| Five external-authentication context records' `ISystemClock Clock` | Use positional member `TimeProvider TimeProvider`; update creation, property reads, named arguments, deconstruction and `with` expressions. |
| `ActivityTestFixture.IFixtureSystemClock` and DI clock mocks | Remove the mock interface; register an explicitly initialized `FakeTimeProvider` as `TimeProvider`. |

The five records are `ConnectionValidationContext`, `ExternalAuthorizationContext`,
`ExternalCallbackContext`, `ConnectionTestContext`, and `ExternalLogoutContext` in
[`ExternalAuthenticationContracts`](../../src/modules/Elsa.ExternalAuthentication/Contracts/ExternalAuthenticationContracts.cs).
Their constructor and generated member signatures change; consumers and provider
adapters must rebuild. Do not claim source or binary compatibility for this release.

### Behavioral invariants

- Replace `clock.UtcNow` with `timeProvider.GetUtcNow()` without changing the
  number or location of reads, precision, nullable behavior, or expiry operators.
  Keep `DateTimeOffset` on persisted models, bookmarks, JSON and public results.
- Preserve existing `.UtcDateTime`, `.DateTime`, Unix-time and tick conversions
  in the initial rewrite. Where a receiving API needs UTC `DateTime`, use
  `.UtcDateTime`; any correction of an existing `.DateTime` use needs its own
  characterization and explicit change. Do not substitute local time.
- For EF expressions, evaluate provider reads before constructing a query or
  update expression and capture a scalar timestamp. Verify existing evaluation
  frequency and SQL parameterization instead of assuming arbitrary provider
  methods translate. Preserve the single captured UTC instant in atomic consume
  operations and their strict expiry comparison.
- Wall-clock UTC instants remain the basis for persisted deadlines and distributed
  coordination. Do not persist `GetTimestamp()` values or compare them between
  processes. Changing duration metrics to `GetElapsedTime()` is separate work.
- `Task.Delay`, `CancelAfter`, `WaitAsync`, `PeriodicTimer`, `System.Timers.Timer`
  and `System.Threading.Timer` keep their current behavior in this migration.
  Advancing a fake provider does not drive these existing real timers.

### Alternatives and consequences

Retaining the old abstraction permanently would preserve an unnecessary public
contract and two override mechanisms. A bidirectional adapter would risk recursive
resolution and disagreeing clocks. Adding compatibility overloads to every public
consumer would increase API and DI complexity. The authorized breaking release
allows their removal after a short-lived, one-way implementation bridge.

Combining UTC-read migration with timer replacement would also change callback,
cancellation, disposal and cadence semantics. Keep those changes separate. This
decision improves standardization and deterministic testing; it makes no
performance, distributed clock synchronization, or universal virtual-time claim.

## Incremental implementation

The following sequence defines the implementation boundaries. Carry constructor callers,
explicit factories and tests with the owning change. Use one conventional commit
per row. Shared composition changes require the full suite before release even
when targeted checks are used while iterating.

### Group A: establish composition, then migrate the core

| Step / suggested commit | Bounded change | Required evidence |
| --- | --- | --- |
| 1. `feat(common): introduce canonical TimeProvider registration` | Add `TimeProviderFeature` and the temporary `SystemClock` facade; make the old feature depend on it. Add the centrally versioned test package and regenerate locks. Keep all existing consumers on the facade. | Common DI tests: default, custom override before/after composition, repeat application, singleton scope validation, Resilience order and sub-second fidelity. |
| 2. `test(time): compose fixtures around a controlled provider` | Update `ActivityTestFixture` and repository DI overrides to register `FakeTimeProvider` as `TimeProvider`; keep the temporary legacy facade in manually constructed fixture containers. Update dependent activity/scheduling tests together. | Existing tests retain their intended instants. Independent fixtures do not share mutable fake clocks. Direct-constructor legacy mocks may remain until their consumers migrate. |
| 3. `refactor(dashboard): use TimeProvider for range resolution` | Replace the dependency and reads in `DashboardRangeResolver`, including direct callers. Use this small leaf service as the first production example. | Dashboard unit tests pin range boundaries and UTC values. |
| 4. `refactor(workflows): migrate execution context time APIs` | Migrate both execution contexts, partial files, core middleware, bookmark/context extensions and periodic commit strategy. Update `UserTask` activity accesses to the context property and direct context constructors in tests in the same commit. | Core and activity tests pin created/updated/completed/log/bookmark instants and periodic commit boundaries; UserTasks compiles against the new public property. |

Review A checks provider precedence, fixture consistency, cross-module compilation
and the public API diff. Do not proceed with two clocks in a shared container.

### Group B: management and runtime services

| Step / suggested commit | Bounded change | Required evidence |
| --- | --- | --- |
| 5. `refactor(management): use TimeProvider for workflow timestamps` | `WorkflowDefinitionPublisher` and `WorkflowInstanceFactory`, callers and tests. | Published and instantiated timestamps retain their type, precision and read timing. |
| 6. `refactor(alterations): use TimeProvider for alteration timestamps` | `AlterationLog`, managers, runners, activities and submit endpoint in the two Alterations modules. | Alteration logging and workflow integration tests; no change to persisted log shape. |
| 7. `refactor(runtime): migrate queue and outbox timestamps` | Bookmark queue store/processor/purger/dead-letter manager, dispatch outbox/processor and stimulus inbox. | Queue eligibility, retries, retention and outbox tests at existing boundaries; preserve atomic operations and due instants. |
| 8. `refactor(runtime): migrate lifecycle clock dependencies` | Remaining Runtime consumers: registries, quiescence, drain, admin service, definition populator, workflow heartbeat and interrupted-workflow restart. Update the explicit `QuiescenceSignal` factory in `WorkflowRuntimeFeature`. | Runtime drain/recovery/heartbeat tests; retain real waits and cancellation mechanisms, including wall-clock duration calculations. |

Review B checks timestamp persistence, recovery, deadline predicates and all
Runtime factory registrations. Perform the workflow PostgreSQL recovery gate
before considering this group ready for release.

### Group C: hosting and authentication contracts

| Step / suggested commit | Bounded change | Required evidence |
| --- | --- | --- |
| 9. `refactor(hosting): use TimeProvider for heartbeat timestamps` | Instance heartbeat writer and monitor. Preserve their timers and stale-instance predicates. | Hosting unit tests pin stale/healthy boundaries and shutdown behavior. |
| 10. `refactor(identity): use TimeProvider for token timestamps` | `DefaultElsaTokenService`, `RoleSecurityNotifier`, `SessionRevoker`; remove the legacy clock constructor on `DefaultAccessTokenIssuer` and update callers. | Identity tests preserve issuance, expiry encoding and session revocation. Direct `DateTime.UtcNow` in `IdentityTokenOptions.ValidateLifetime` remains a separately tracked limitation. |
| 11. `refactor(auth): expose TimeProvider in extension contexts` | Change the five positional records and their entire construction/read chain: broker, preview sign-in, connection test/management services and provider adapter tests. Change owning constructor dependencies when required to pass the canonical provider. | External-auth and OpenIdConnect adapter tests compile with new record members, named arguments, deconstruction and `with` usage; callback/logout behavior is unchanged. |
| 12. `refactor(auth): migrate remaining in-memory clock consumers` | Remaining external-auth services, token issuer, validity assessor, identity link management, provisioner and four in-memory stores. | Existing state/grant/session/preview-result tests preserve exact expiry, refresh rotation and single-use consumption rules. |

Review C checks the authentication extension API, token boundaries and explicitly
remaining system-time dependencies. A controlled token issuer alone does not make
the framework JWT validator or application lifetime delegate use fake time.

### Group D: database and user-task consumers

| Step / suggested commit | Bounded change | Required evidence |
| --- | --- | --- |
| 13. `refactor(auth): migrate PostgreSQL clock dependencies` | EF atomic stores, session store and identity provisioner. Capture timestamps outside EF expression trees at the existing operation boundary. | PostgreSQL conformance/integration tests verify generated queries execute, consume races remain atomic, refresh reuse is detected, revocation values and strict expiry rules remain intact. |
| 14. `refactor(user-tasks): migrate service clock dependencies` | Managers, due/invitation/projection/reconciliation services, guest security, bookmark handler, endpoints and worker resolution. | UserTasks tests pin creation, due, claim, invitation and revocation timestamps; existing `PeriodicTimer` cadence remains real. |
| 15. `refactor(user-tasks): migrate PostgreSQL guest clock reads` | Guest session store and invitation outbox in `EFCoreUserTaskGuestStores`; capture values before `ExecuteUpdate`. | PostgreSQL user-task conformance tests prove revocation updates and guest expiry/claim behavior; no schema changes. |
| 16. `refactor(http): use TimeProvider for zip cache expiry` | `ZipManager` and its activity tests. | Preserve the existing `>` expiry comparison, including exact equality, with controlled UTC time. |

Review D checks SQL translation, single-use/concurrency behavior, data formats and
durable test evidence. A passing in-memory suite does not satisfy this review.

### Group E: scheduling calculations and removal

| Step / suggested commit | Bounded change | Required evidence |
| --- | --- | --- |
| 17. `refactor(common): migrate recurring schedule clock reads` | `RecurringTaskScheduleManager` and `CronSchedule`; preserve timer implementation and existing read/conversion behavior. | Characterize cron conversion behavior first. If `.DateTime` needs a fix, land it as a separate `fix(common)` commit before this rewrite, with UTC-kind and occurrence tests. |
| 18. `refactor(scheduling): use TimeProvider for due-time calculations` | `CronosCronParser`, `DefaultTriggerScheduler`, StartAt and Delay/Timer context extensions. | Scheduling/activity tests cover UTC cron occurrence, one tick before/at/after due time, and unchanged bookmark payloads. |
| 19. `refactor(scheduling): migrate scheduled task clock dependencies` | The three `Scheduled*Task` classes and their constructor/factory tests. Change UTC reads only; keep `System.Timers.Timer`, catch-up staggering, minimum delay and rearming behavior. | Existing scheduled task and disposal tests pass. Recurrence still computes the next start from callback-time UTC plus interval. |
| 20. `refactor(common)!: remove the legacy system clock API` | Retarget remaining feature dependencies; remove `ISystemClock`, `SystemClock`, `SystemClockFeature` and obsolete fixture mock interfaces. Update host examples and migration guidance. | All source/test categories and downstream examples compile without legacy symbols; full .NET 10/11 suite and release gates pass. Commit footer describes the breaking API and registration changes. |

Review E checks final removal, release guidance, and timer semantics. If a group
needs a separate correctness repair, keep it separately reviewable and perform
the review after at most four commits rather than stretching the group.

### Host and extension upgrade

Default hosts need no custom clock registration. Hosts with a custom legacy
implementation must derive from `TimeProvider`, override `GetUtcNow()`, and
register the instance as the base service type before building the container:

```csharp
services.AddSingleton<TimeProvider>(customTimeProvider);
services.AddElsa(elsa => { /* existing feature configuration */ });
```

Replace constructor parameters and `context.SystemClock.UtcNow` with the APIs in
the table above. External-authentication extensions use
`context.TimeProvider.GetUtcNow()`. Rebuild all custom activities and provider
adapters, update clock mocks, and update the whole Flamme package graph together;
old binaries compiled against deleted types cannot be mixed with new packages.

No stored workflow/bookmark/schema migration is intended. Keep activity names,
versions, JSON fields and persisted instants stable, and prove old persisted
instances can resume with the new binaries. Rollback means redeploying the
previous coherent application/package set, not selectively restoring an old
clock package. Persistence compatibility is a release gate, not an assumption.

## Validation and release gates

### Deterministic time tests

Use a fresh `FakeTimeProvider` per test with an explicit UTC starting instant and
`AutoAdvanceAmount = TimeSpan.Zero`. Register it as `TimeProvider`, not only as its
concrete type. Call `Advance` deliberately between operations. Where existing
tests verify sequential clock reads, retain the scenario with a small scripted
provider; replacing every sequential mock with a frozen clock would lose coverage.

Use the namespace `Microsoft.Extensions.Time.Testing`. For example, replace a
mock configured to return one UTC instant with this fixture arrangement:

```csharp
var initialUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
var fakeTime = new FakeTimeProvider(initialUtc)
{
    AutoAdvanceAmount = TimeSpan.Zero
};
services.AddSingleton<TimeProvider>(fakeTime);
// Build the fixture/container, perform the operation, and assert its timestamp.
fakeTime.Advance(TimeSpan.FromMinutes(5));
// Perform the next operation and assert its timestamp or expiration outcome.
```

Await TUnit assertions against observable domain results. Replace tests that
directly construct a migrated service with `new ServiceUnderTest(fakeTime, ...)`;
DI-based tests supply the same instance through the fixture configuration hook.
Remove each old mock when its consumer changes. Do not replace an unrelated mock
or introduce `Task.Delay` just to make a time assertion pass.

| Contract | Acceptance checks |
| --- | --- |
| Composition | Full `AddElsa` and minimal fixture containers resolve the same provider for all consumers. Defaults, repeated feature application, both Resilience orders and host overrides before/after `AddElsa` pass. Use `ValidateOnBuild` and `ValidateScopes`, and explicitly resolve factory-created singletons: build validation alone cannot inspect dependencies inside factories. |
| API construction | Exercise normal DI and `ActivatorUtilities` paths, explicit runtime/scheduling factories, direct constructors and external-authentication records. Compile affected dependent projects, not just the edited module. |
| UTC values | Preserve fractional-second ticks and `DateTimeOffset` values in memory. Test a non-UTC host time zone without substituting local time. Verify `.UtcDateTime.Kind == Utc` at existing UTC conversion boundaries. |
| Workflow state | With controlled instants, execute, suspend, persist, rebuild the host/provider, resume and complete. Verify created/updated/completed/log/bookmark fields, old serialized shape and original due instants. Read legacy-version fixtures and exercise PostgreSQL recovery. |
| Expiration | In memory, check one tick before, exactly at, and one tick after expiration using each consumer's existing comparison. PostgreSQL tests use instants representable by the mapped column precision; tick-backed columns retain exact tick boundaries. Never assert sub-microsecond timestamp round-trips that PostgreSQL cannot preserve. |
| Atomicity | With two callers racing to consume the same grant/state/invitation, retain one success. Preserve refresh-generation/reuse and revoked-session behavior. Verify updates execute on PostgreSQL, with captured time values and unchanged transaction boundaries. |
| Scheduling | Verify future/past/exact-now calculations, cron boundary crossing, stagger/minimum-delay rules and callback-based recurrence. Existing real-timer disposal, cancellation, exception and non-overlap tests remain required. |
| Scope limit | Fake advancement controls UTC reads in this release. It does not establish virtual behavior for drain waits, heartbeat timers, scheduled callbacks, JWT framework validation or third-party libraries. |

For later timer work, require provider-aware waits/timers, cancellation before and
during a callback, async callback completion signals, no callbacks/rearming after
disposal, and a real watchdog timeout to prevent hanging tests. `FakeTimeProvider`
advancement alone is not proof that an asynchronous callback has completed. Do
not adopt a periodic timer without deciding whether recurrence is fixed-rate,
relative to callback start, or relative to callback completion.

### Build commands

Use the pinned SDK and the native solution/test runner. Restore all target
frameworks before using a framework-specific `--no-restore` command. Dependency
changes in step 1 require `dotnet restore Elsa.slnx --force-evaluate`, followed by
review of central package and executable lockfile changes. Thereafter validate
locked restore:

```bash
dotnet --info
dotnet msbuild src/modules/Elsa.Common/Elsa.Common.csproj \
  -p:TargetFramework=net11.0 \
  -getProperty:TargetFramework,NETCoreSdkVersion,UseRuntimeAsync
dotnet restore Elsa.slnx --locked-mode

# Example targeted iteration; replace the project for the owning slice.
dotnet test --project test/unit/Elsa.Common.UnitTests/Elsa.Common.UnitTests.csproj \
  --configuration Release --no-restore

# Cross-module and release gates, with PostgreSQL/container prerequisites ready.
dotnet build Elsa.slnx --configuration Release --no-restore
dotnet test --solution Elsa.slnx --configuration Release --framework net10.0 --no-build
dotnet test --solution Elsa.slnx --configuration Release --framework net11.0 --no-build
```

Use TUnit on Microsoft Testing Platform; do not introduce VSTest or Coverlet
settings. Runner switches precede `--`; application switches follow it. Keep the
existing CI coverage policy. See [CI guidance](../ci.md) and the
[testing strategy](../qa/test-guidelines.md) for environment requirements.

For affected async infrastructure, also rebuild and test the .NET 11 opt-out;
changing the property with `--no-build` alone would test the wrong assemblies:

```bash
dotnet build test/unit/Elsa.Workflows.Runtime.UnitTests/Elsa.Workflows.Runtime.UnitTests.csproj \
  --configuration Release --framework net11.0 --no-restore --no-incremental \
  -p:UseRuntimeAsync=false
dotnet test --project test/unit/Elsa.Workflows.Runtime.UnitTests/Elsa.Workflows.Runtime.UnitTests.csproj \
  --configuration Release --framework net11.0 --no-build -p:UseRuntimeAsync=false
```

Rebuild with the default property before subsequent default-mode validation or
packaging. Record mode, framework, passed/failed/skipped test totals and any
environment limits in PR evidence. A skipped PostgreSQL test is not durable
persistence evidence. This proposal does not claim the release gates have run.

### Final removal and release review

Run the legacy search against tracked C# files, including performance tests and
shared fixtures. Any remaining hit must be removed or explained by a genuinely
unrelated external contract; there is no Elsa compatibility allowlist:

```bash
git grep -n -w -E 'ISystemClock|SystemClock|SystemClockFeature' -- '*.cs'
git grep -n -F '.Clock' -- '*.cs'
git grep -n -w 'TimeProvider' -- '*.cs'
git diff --check
scripts/adr/generate-toc.sh --check
```

The first search should return no Elsa legacy usages (exit code 1 means no
matches). Inspect the remaining searches' record callers, registrations and any
`TimeProvider.System` uses; production services should consume DI, while defaults
belong at composition boundaries. Update live code examples; retain historical
mentions in this ADR and the release's upgrade guidance as explanations.

Publish the complete package graph as a breaking release only after API changes,
default host startup, full test matrix, PostgreSQL recovery/conformance and
constructor activation checks pass. No database migration is planned; any test
showing a persisted format change stops the release until that change is removed
or receives its own migration design. Keep review findings and command logs in
the PR or external evidence rather than appending execution ledgers to this ADR.
