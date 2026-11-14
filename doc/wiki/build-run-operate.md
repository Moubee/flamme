# Build, Run, And Operate

This page collects day-to-day build, run, and operational notes for Elsa Core contributors.

## Build Commands

Install the SDK selected by [global.json](../../global.json): .NET SDK 11.0.100 with patch roll-forward and prereleases disabled. Install the .NET 10 SDK/runtime alongside it to run tests and application hosts. Library target frameworks remain `net8.0`, `net9.0`, and `net10.0`.

Restore first when working from a clean checkout or after dependency changes. Use `--ignore-failed-sources` when packages are available from other configured sources despite an unavailable feed:

```bash
dotnet restore Elsa.slnx --ignore-failed-sources
```

Build the solution (restore is implicit unless `--no-restore` is supplied):

```bash
dotnet build Elsa.slnx
```

For an explicit restore/build sequence:

```bash
dotnet restore Elsa.slnx --ignore-failed-sources
dotnet build Elsa.slnx --no-restore
```

Run the TUnit suite directly through Microsoft Testing Platform:

```bash
dotnet restore Elsa.slnx --ignore-failed-sources
dotnet test --solution Elsa.slnx --no-restore
```

Targeted test project:

```bash
dotnet restore test/unit/Elsa.Workflows.Core.UnitTests/Elsa.Workflows.Core.UnitTests.csproj --ignore-failed-sources
dotnet test --project test/unit/Elsa.Workflows.Core.UnitTests/Elsa.Workflows.Core.UnitTests.csproj --no-restore
```

ElsaScript DSL tests:

```bash
dotnet test --project test/integration/Elsa.Dsl.ElsaScript.IntegrationTests/Elsa.Dsl.ElsaScript.IntegrationTests.csproj
```

## Build System

The .NET CLI invokes MSBuild directly. Shared settings live in [Directory.Build.props](../../Directory.Build.props) and [Directory.Build.targets](../../Directory.Build.targets). Builds default to Debug, while the CI workflows explicitly select Release. Pass `--configuration Debug` or `--configuration Release` to select a configuration explicitly.

Clean the selected configuration with `dotnet clean Elsa.slnx --configuration Release`. Standard build outputs remain in each project's `bin/` and `obj/` directories, and NuGet packages go to `packages/`. Cleaning build outputs does not remove previously created packages.

Tests use TUnit on Microsoft Testing Platform. Run the full suite with `dotnet test --solution Elsa.slnx`, or select one test project with `dotnet test --project <path-to-test.csproj>`.

Source projects multi-target `net8.0`, `net9.0`, and `net10.0` through [src/Directory.Build.props](../../src/Directory.Build.props). Central package versions are in [Directory.Packages.props](../../Directory.Packages.props), including conditional version blocks for .NET 8/9 and .NET 10.

## Run The Reference Server

The main sample host is [src/apps/Elsa.Server.Web](../../src/apps/Elsa.Server.Web). It wires most major modules in [Program.cs](../../src/apps/Elsa.Server.Web/Program.cs).

Start PostgreSQL with `docker compose -f docker/docker-compose.yml up -d postgres` from the repository root. The checked-in `ConnectionStrings:PostgreSql` matches that development service; override it for other environments. Custom hosts can omit EF Core persistence registrations to use process-local in-memory stores.

Restore the project and run it with:

```bash
dotnet restore src/apps/Elsa.Server.Web/Elsa.Server.Web.csproj --ignore-failed-sources
dotnet run --project src/apps/Elsa.Server.Web/Elsa.Server.Web.csproj --no-restore
```

Notable toggles in `Program.cs`:

- `useReadOnlyMode`
- `useSignalR`
- `useStructuredLogs`
- `useMultitenancy`
- `disableVariableWrappers`

The sample configures identity, default authentication, workflow management/runtime with PostgreSQL, workflow API, fluent storage, ElsaScript blob storage, scheduling, C#, JavaScript, Python, Liquid, HTTP, and optional tenants/structured logs.

When running outside the explicit `Development` or `Demo` environments, configure a secure random JWT signing key with at least 32 ASCII characters. For the code-first reference server, prefer `Identity__Tokens__SigningKey` from an environment variable or secrets manager instead of committing the value to appsettings. Shell-based hosts use the shell feature path, for example `CShells__Shells__Default__Features__Identity__SigningKey`.

## Docker Quick Try

Build and run the reference server from the repository root, as documented in the
[README](../../README.md):

```bash
docker build -f docker/ElsaServer.Dockerfile -t elsa-server:local .
docker run --rm -it \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e HTTP_PORTS=8080 \
  -e HTTP__BASEURL=http://localhost:13000 \
  -p 13000:8080 \
  elsa-server:local
```

Host [Elsa Studio](https://github.com/elsa-workflows/elsa-studio) separately when a visual designer is needed.

Production containers must inject a secure JWT signing key through environment variables or a secrets manager. The appsettings placeholder and known public sample keys are rejected during startup outside `Development` or `Demo`.

Default development login is available only when a development configuration explicitly provisions it:

```text
Username: admin
Password: password
```

Do not use development credentials in production.

## ASP.NET Middleware Order

The reference server pipeline is a useful ordering guide:

1. developer exception page in development
2. CORS
3. health checks
4. routing
5. authentication
6. authorization
7. tenants
8. workflow API
9. JSON serialization error handler
10. workflow HTTP endpoint middleware
11. controllers
12. Swagger UI in development
13. SignalR workflow hubs if enabled
14. structured logs hub if enabled

See [Program.cs](../../src/apps/Elsa.Server.Web/Program.cs).

## Operational Endpoints

With default route prefix `elsa/api`, runtime admin endpoints include:

- `GET /elsa/api/admin/workflow-runtime/status`: requires `read:workflow-runtime`; `ManageWorkflowRuntime` is also accepted for backward compatibility.
- `POST /elsa/api/admin/workflow-runtime/pause`: requires `ManageWorkflowRuntime`.
- `POST /elsa/api/admin/workflow-runtime/resume`: requires `ManageWorkflowRuntime`.
- `POST /elsa/api/admin/workflow-runtime/force-drain`: requires `ManageWorkflowRuntime`.

Structured log diagnostics endpoints include:

- `GET|POST /elsa/api/diagnostics/structured-logs/recent`
- `GET /elsa/api/diagnostics/structured-logs/sources`
- `GET /elsa/api/diagnostics/structured-logs/storage`

Console log diagnostics endpoints include (when `Elsa.Diagnostics.ConsoleLogs` is enabled):

- `POST /elsa/api/diagnostics/console-logs/recent`
- `GET /elsa/api/diagnostics/console-logs/sources`

Health checks are mapped to `/` and `/health/live` for process liveness and `/health/ready` for Elsa runtime readiness in the reference server. See [Health Checks](health-checks.md) for Kubernetes liveness/readiness recommendations and Elsa-specific runtime readiness probes.

## Runtime Knobs

Common runtime-related options in the reference host:

- `RuntimeOptions.InactivityThreshold`
- `BookmarkQueuePurgeOptions.Ttl`
- `CachingOptions.CacheDuration`
- `IncidentOptions.DefaultIncidentStrategy`
- recurring task schedules for trigger queue, bookmark queue purge, and interrupted workflow restart

Structured logs options include recent log capacity, query size, source heartbeat timeout, redaction settings, and storage provider options.

## Local Development Notes

- Prefer targeted builds/tests while iterating.
- Use `rg` to find feature registration and endpoint routes.
- Keep package version changes centralized in [Directory.Packages.props](../../Directory.Packages.props).
- Avoid provider-specific assumptions in core modules.
- When changing middleware, verify both code-first host setup and shell-feature setup if applicable.

## Release And Package Notes

Create packages with an explicit version using the standard pack command:

```bash
dotnet pack Elsa.slnx --configuration Release --output packages -p:Version=3.9.0-preview.1
```

This restores and builds before packing. Use `--no-build` only when the same configuration and version have already been built. Package versions come from project metadata or the explicit `Version` MSBuild property; no timestamp suffix is added automatically. The package workflow supplies its release tag or preview version and uploads both `.nupkg` and `.snupkg` files from `packages/`.

Because source projects multi-target three frameworks, package upgrades should be checked against all target frameworks and provider packages. Persistence changes usually need extra scrutiny because each provider package may need migrations or compatibility updates.
