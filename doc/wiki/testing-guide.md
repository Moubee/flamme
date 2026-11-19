# Testing Guide

Flamme uses TUnit on Microsoft Testing Platform (MTP) for unit, integration, and component tests. Performance benchmarks under `test/performance` use BenchmarkDotNet instead. The best test choice depends on what boundary you are changing.

The detailed internal testing strategy is [doc/qa/test-guidelines.md](../qa/test-guidelines.md). This page is a wiki-sized map.

## Test Folders

| Folder | Purpose |
| --- | --- |
| [test/unit](../../test/unit) | Isolated services, activities, converters, stores, validators, descriptors, and small logic. |
| [test/integration](../../test/integration) | In-process composition of workflow engine services, expressions, activities, runtime behavior, and module integration. |
| [test/component](../../test/component) | Host-level workflows, persistence-backed scenarios, HTTP workflows, clustered behavior, and lifecycle behavior. |
| [test/performance](../../test/performance) | Benchmark and throughput scenarios. |

Shared helpers:

- [Flamme.Testing.Shared](../../src/common/Flamme.Testing.Shared)
- [Flamme.Testing.Shared.Integration](../../src/common/Flamme.Testing.Shared.Integration)
- [Flamme.Testing.Shared.Component](../../src/common/Flamme.Testing.Shared.Component)

## Choosing A Test Type

| Change | Preferred test |
| --- | --- |
| Activity logic without persistence or scheduler | Unit test with `ActivityTestFixture`. |
| Expression evaluator/parser behavior | Unit test or language-specific integration test. |
| Workflow execution semantics | Integration test with workflow runner/test fixture. |
| Bookmarks, triggers, runtime dispatch, recovery | Integration test; component test if host lifecycle or persistence matters. |
| API endpoint shape or authorization | Unit/integration endpoint test if existing pattern exists; component test for host-level behavior. |
| EF Core store or migration | Provider-specific integration/component test. |
| HTTP workflows | Component test under HTTP workflow scenarios. |
| Workflow trace/metric instrumentation | `Flamme.Workflows.Core.UnitTests` instrumentation tests. |
| Test-run logs and telemetry | TUnit native logging and ASP.NET Core integration. |
| Dashboard API endpoints or aggregation logic | `Flamme.Dashboard.Api.UnitTests`. |
| AI/Weaver abstractions or host behavior | `Flamme.AI.Abstractions.UnitTests`, `Flamme.AI.Host.UnitTests`, or `Flamme.AI.IntegrationTests`. |

## Useful Commands

Use the SDK selected by [global.json](../../global.json) and install the .NET 10 SDK/runtime for test applications. Restore first when starting from a clean checkout or after dependency changes:

```bash
dotnet restore Flamme.slnx --ignore-failed-sources
```

Build the solution with direct `dotnet` commands:

```bash
dotnet restore Flamme.slnx --ignore-failed-sources
dotnet build Flamme.slnx --no-restore
```

Run all tests with direct `dotnet` commands:

```bash
dotnet restore Flamme.slnx --ignore-failed-sources
dotnet test --solution Flamme.slnx --no-restore
```

Run targeted projects:

```bash
dotnet restore test/unit/Flamme.Workflows.Core.UnitTests/Flamme.Workflows.Core.UnitTests.csproj --ignore-failed-sources
dotnet test --project test/unit/Flamme.Workflows.Core.UnitTests/Flamme.Workflows.Core.UnitTests.csproj --no-restore

dotnet restore test/integration/Flamme.Workflows.IntegrationTests/Flamme.Workflows.IntegrationTests.csproj --ignore-failed-sources
dotnet test --project test/integration/Flamme.Workflows.IntegrationTests/Flamme.Workflows.IntegrationTests.csproj --no-restore

dotnet restore test/component/Flamme.Workflows.ComponentTests/Flamme.Workflows.ComponentTests.csproj --ignore-failed-sources
dotnet test --project test/component/Flamme.Workflows.ComponentTests/Flamme.Workflows.ComponentTests.csproj --no-restore

```

## Activity Tests

Use `ActivityTestFixture` from [Flamme.Testing.Shared](../../src/common/Flamme.Testing.Shared). Activity tests should assert activity outputs, variables, scheduled child activities, outcomes, and fault behavior without needing a host.

Examples live in:

- [test/unit/Flamme.Activities.UnitTests](../../test/unit/Flamme.Activities.UnitTests)
- [test/unit/Flamme.Workflows.Core.UnitTests](../../test/unit/Flamme.Workflows.Core.UnitTests)

## Workflow Integration Tests

Use integration helpers when activity behavior depends on workflow runner behavior, variables, activity outputs, expressions, or bookmarks.

Examples:

- [test/integration/Flamme.Workflows.IntegrationTests](../../test/integration/Flamme.Workflows.IntegrationTests)
- [test/integration/Flamme.Activities.IntegrationTests](../../test/integration/Flamme.Activities.IntegrationTests)
- [test/integration/Flamme.JavaScript.IntegrationTests](../../test/integration/Flamme.JavaScript.IntegrationTests)

## Component Tests

Component tests use host fixtures. The base class is [AppComponentTest](../../test/component/Flamme.Workflows.ComponentTests/Helpers/Abstractions/AppComponentTest.cs). It creates a scope, pushes the default tenant context, tracks in-flight workflows, and waits for them to idle on dispose.

Fixture landmarks:

- [App](../../test/component/Flamme.Workflows.ComponentTests/Helpers/Fixtures/App.cs)
- [Cluster](../../test/component/Flamme.Workflows.ComponentTests/Helpers/Fixtures/Cluster.cs)
- [WorkflowServer](../../test/component/Flamme.Workflows.ComponentTests/Helpers/Fixtures/WorkflowServer.cs)
- [Infrastructure](../../test/component/Flamme.Workflows.ComponentTests/Helpers/Fixtures/Infrastructure.cs)

Use component tests when host lifecycle, HTTP server behavior, actual persistence, distributed runtime behavior, or multiple services working together are essential to the assertion.

## Logs And Telemetry

TUnit owns test-run log capture, correlation, and telemetry. Shared integration hosts use the official TUnit logging provider, and TUnit ASP.NET Core integration configures application telemetry. Do not add Flamme log-capture providers, in-memory exporters, or telemetry receivers.

`TestWebApplicationFactory` registers logging automatically for its application host.
`TestApplicationBuilder` constructs a plain `ServiceCollection`, so it references
`TUnit.Logging.Microsoft` and explicitly adds the official correlated provider. TUnit's
automatic console capture alone does not register an `ILogger` provider in this container.
Set both the logging filter and the provider minimum to Debug to retain diagnostic output.
See TUnit's [standalone logging documentation](https://github.com/thomhurst/TUnit/blob/77453eab9cc452dbba8c53e77d12bf595f71cf30/docs/docs/extending/logging.md#standalone-no-aspnet-core),
which matches the source commit of the installed TUnit 1.84.0 packages.

The removed diagnostics modules and their test projects are no longer part of the solution. The retained [workflow instrumentation tests](../../test/unit/Flamme.Workflows.Core.UnitTests/Telemetry/WorkflowInstrumentationTests.cs) verify the `Elsa.Workflows` source and meter contract with native .NET listeners.

## Good Test Hygiene

- Prefer targeted project tests while iterating.
- Add a regression test for a bug before or alongside the fix.
- Keep arrange/setup in constructors or small helpers when it repeats.
- Use `IAsyncDisposable` or TUnit lifecycle hooks for async teardown.
- Avoid sleeps when a deterministic signal or store assertion is available.
- For multi-targeting issues, consider whether all target frameworks need coverage.
- When changing shared runtime or persistence behavior, finish with a broader build/test run if feasible.
