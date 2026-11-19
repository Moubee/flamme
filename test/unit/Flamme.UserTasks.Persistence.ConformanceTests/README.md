# User Tasks persistence conformance suite

One suite, run unchanged against every implementation of the User Tasks persistence contracts. It exists
because the User Tasks build shipped four P1 defects that a single-threaded, happy-path, in-memory test
suite could not see — every one of them was found by injecting a failure against a real store.

## What it covers

| Contract | Suite | Implementations |
| --- | --- | --- |
| `IUserTaskRepository` | `UserTaskRepositoryConformanceTests` | InMemory, PostgreSQL EF Core |
| `IUserTaskGuestSessionIssuer` | `UserTaskGuestSessionConformanceTests` | InMemory, PostgreSQL EF Core |
| `IUserTaskInvitationOutbox` | `UserTaskInvitationOutboxConformanceTests` | InMemory, PostgreSQL EF Core |
| The services above them | `UserTaskFaultInjectionConformanceTests` | InMemory, PostgreSQL EF Core |

`UserTaskFaultInjectionConformanceTests` runs the real `DefaultUserTaskManager` and
`DefaultUserTaskInvitationService` against a real store and breaks the seams between them with the
decorators in `Faults/`. A cross-store operation must either commit fully or leave the caller able to
retry, and the retry must converge.

## Providers

Both in-memory and PostgreSQL are required in every conformance run. PostgreSQL uses
`PostgreSqlTestDatabase` to create an isolated database on the `ELSA_TEST_POSTGRES` server, or starts a
shared PostgreSQL test container when that server variable is unset. The fixture drops only the isolated
database it created. A working Docker engine or the configured PostgreSQL test server is required;
missing infrastructure fails the suite instead of silently skipping relational coverage.

```bash
dotnet test --project test/unit/Flamme.UserTasks.Persistence.ConformanceTests/Flamme.UserTasks.Persistence.ConformanceTests.csproj
```

To use an existing **disposable** database directly, supply the optional conformance-specific override:

```bash
ELSA_USERTASKS_TEST_POSTGRES="Host=localhost;Database=elsa_conformance;Username=elsa;Password=elsa" \
  dotnet test --project test/unit/Flamme.UserTasks.Persistence.ConformanceTests/Flamme.UserTasks.Persistence.ConformanceTests.csproj
```

The override database is migrated and each test uses its own tenant, but the suite never drops that
database. An explicitly empty or whitespace-only override is invalid: unset it to select the isolated
default, or provide a valid connection string.

`ConformanceCoverageTests` always runs. It fails if a provider that must run is unreachable, fails if a
override is set but empty or whitespace, and writes the full matrix to the
test output and to `user-task-conformance-coverage.md` in the output directory.

## Maintaining provider coverage

1. Keep `ConformanceProviders.All` limited to the supported in-memory and PostgreSQL implementations.
2. Add an `IAsyncInitializer` fixture under `Providers/` whose constructor does not connect to the provider.
3. Add a stable provider key to `ProviderFixtureKeys`.
4. Add one concrete class per supported contract to `ProviderConformanceSuites.cs`. Put all four native
   TUnit attributes directly on every concrete class: `[InheritsTests]`, a keyed
   `[ClassDataSource<TFixture>(Shared = SharedType.Keyed, Key = ...)]`, `[NotInParallel(...)]` with the
   same key, and `[ConformanceProvider(...)]`.

An unavailable provider is skipped before its fixture initializes. A conformance class without
`[ConformanceProvider]` is also skipped with a wiring error rather than counted as coverage — the suite
refuses to guess which provider a class exercised.
