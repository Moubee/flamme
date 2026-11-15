# Migrate the Elsa clock abstraction to TimeProvider

## Status

Proposed for a breaking release; breaking API changes are authorized. This record
specifies the migration; it does not change production
registrations, public APIs, scheduling behavior, or persisted data. Acceptance
requires the implementation and release gates described below.

## Context and research

The baseline is `origin/main` at `239ae5b99`. Flamme has its own
[`Elsa.Common.ISystemClock`](../../src/modules/Elsa.Common/Contracts/ISystemClock.cs),
with one member: `DateTimeOffset UtcNow`. Its
[`SystemClock`](../../src/modules/Elsa.Common/Services/SystemClock.cs) returns
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
Select a stable version compatible with both targets through central package
management when introducing it, and regenerate all executable dependency locks
according to [contributor guidance](../../CONTRIBUTING.md#dependency-updates).
The package and its version are not added by this design record.

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
