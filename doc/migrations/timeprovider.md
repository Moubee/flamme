# Upgrade clock dependencies to TimeProvider

Flamme uses `System.TimeProvider` for injected UTC time. Upgrade all Flamme
packages together and rebuild custom activities, authentication adapters and
other extensions. This is a breaking API change on both .NET 10 and .NET 11.

## Application services and host configuration

Replace constructor parameters of type `Elsa.Common.ISystemClock` with
`TimeProvider` and replace `clock.UtcNow` reads with `timeProvider.GetUtcNow()`.
The return value remains `DateTimeOffset`; retain existing conversions where an
API needs a `DateTime`, Unix timestamp or tick count.

`ISystemClock`, `Elsa.Common.Services.SystemClock` and `SystemClockFeature` have
been removed. Custom feature dependency attributes should reference
`Flamme.Common.Features.TimeProviderFeature`. Default `AddFlamme` composition provides
`TimeProvider.System`. Application services should receive the provider through
DI. A custom provider must be registered as the base type:

```csharp
services.AddSingleton<TimeProvider>(customTimeProvider);
services.AddFlamme(flamme => { /* feature configuration */ });
```

Register before building the container. A host registration before or after
`AddFlamme` overrides the default; multiple explicit registrations follow DI's
last-registration-wins behavior. Use a singleton provider that supports concurrent
access. Scoped providers cannot be consumed by singleton services.

## Extension APIs

`WorkflowExecutionContext.SystemClock` is now
`WorkflowExecutionContext.TimeProvider`. For example:

```csharp
var now = context.WorkflowExecutionContext.TimeProvider.GetUtcNow();
```

Constructors and factories that formerly took `ISystemClock` now take
`TimeProvider` with parameter name `timeProvider`. The obsolete clock-based
`DefaultAccessTokenIssuer` constructor has been removed; inject `IFlammeTokenService`
through its remaining constructor.

The five external-authentication records `ConnectionValidationContext`,
`ExternalAuthorizationContext`, `ExternalCallbackContext`, `ConnectionTestContext`
and `ExternalLogoutContext` now expose a positional `TimeProvider TimeProvider`
member. Replace `context.Clock.UtcNow` with `context.TimeProvider.GetUtcNow()` and
update constructor arguments, deconstruction and `with` expressions accordingly.

## Tests

Use `Microsoft.Extensions.TimeProvider.Testing` 11.0.0 and the namespace
`Microsoft.Extensions.Time.Testing`. The repository centrally references it for
correctness tests and the shared activity fixture. Downstream test projects need
their own package reference.

```csharp
var fake = new FakeTimeProvider(
    new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
services.AddSingleton<TimeProvider>(fake);
// Execute and assert the initial timestamp.
fake.Advance(TimeSpan.FromMinutes(5));
// Execute and assert the later timestamp or expiration outcome.
```

Create a fresh fake per test. `ActivityTestFixture` defaults to its own frozen
provider at Unix epoch; replace it through `ConfigureServices` when another start
instant is required. Existing sequential-read scenarios need an explicitly
stepping/recording provider rather than silently losing their behavior.

## Persistence and timing behavior

Stored timestamp types, activity identities, bookmark payloads and workflow state
formats are preserved. There is no database schema migration. Roll back by
redeploying the previous coherent application/package set.

This migration changes injected UTC reads. Existing real timers, delays,
cancellation deadlines and third-party clock dependencies retain their timing
mechanisms. Advancing `FakeTimeProvider` does not fire those timers. JWT lifetime
validation still has framework/system-time dependencies; a fake token issuer
alone does not control end-to-end token validation.

The separate recurring-schedule correction supplies UTC `DateTime` to Cronos;
the previous `.DateTime` conversion produced `Unspecified` and was rejected.

See the [architecture decision](../adr/2026-11-15-timeprovider-migration.md) for
the migration boundaries and validation requirements.
