# Migrate the Elsa clock abstraction to TimeProvider

## Status

Proposed. This record specifies the migration; it does not change production
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
[`SimulateResponseSessionStore`](../../src/modules/Elsa.Resilience/Services/SimulateResponseSessionStore.cs),
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

1. Establish a single default provider and an explicit compatibility policy for
   legacy registrations, public constructors and workflow context properties.
2. Establish deterministic test-time composition, then migrate UTC reads in small
   module slices with unchanged values, conversions and boundary predicates.
3. Migrate the workflow core and persistence-facing consumers with recovery and
   PostgreSQL validation; move scheduling calculations independently of timers.
4. Retire compatibility APIs only at an explicitly breaking release boundary.
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
