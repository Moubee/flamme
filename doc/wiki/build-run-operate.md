# Build, Run, And Operate

This page collects build, run, and operational notes for Flamme contributors. The [project direction](../project-direction.md) sets Kubernetes as the primary production hosting scenario; the local reference server and container build below are development foundations.

## Build Commands

Install the SDK selected by [global.json](../../global.json): .NET SDK 11.0.100 with patch roll-forward and prereleases disabled. Install the .NET 10 runtime alongside it to run both supported targets. Libraries, app hosts, and correctness tests target `net10.0` and `net11.0`; performance benchmarks target `net10.0`. .NET 8 and .NET 9 are no longer supported.

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

Tests use TUnit on Microsoft Testing Platform. Run the full suite with `dotnet test --solution Elsa.slnx`, or select one test project with `dotnet test --project <path-to-test.csproj>`. Add `--framework net10.0` or `--framework net11.0` to run one target.

Source libraries multi-target `net10.0` and `net11.0` through [src/Directory.Build.props](../../src/Directory.Build.props). Central package versions are in [Directory.Packages.props](../../Directory.Packages.props), including framework-specific package versions where necessary for .NET 10 and .NET 11.

## Run The Reference Server

The main sample host is [src/apps/Elsa.Server.Web](../../src/apps/Elsa.Server.Web). It wires most major modules in [Program.cs](../../src/apps/Elsa.Server.Web/Program.cs).

Start the isolated PostgreSQL development container using the [root README setup](../../README.md#run-locally). The checked-in `ConnectionStrings:PostgreSql` matches that example; override it for other environments. Custom hosts can omit EF Core persistence registrations to use process-local in-memory stores.

Restore the project and run it with:

```bash
dotnet restore src/apps/Elsa.Server.Web/Elsa.Server.Web.csproj --ignore-failed-sources
dotnet run --project src/apps/Elsa.Server.Web/Elsa.Server.Web.csproj --framework net11.0 --no-restore
```

Notable toggles in `Program.cs`:

- `useReadOnlyMode`
- `useSignalR`
- `useStructuredLogs`
- `useMultitenancy`
- `disableVariableWrappers`

The sample configures identity, default authentication, workflow management/runtime with PostgreSQL, workflow API, fluent storage, ElsaScript blob storage, scheduling, C#, JavaScript, Python, Liquid, HTTP, and optional tenants.

When running outside the explicit `Development` or `Demo` environments, configure a secure random JWT signing key with at least 32 ASCII characters. For the code-first reference server, prefer `Identity__Tokens__SigningKey` from an environment variable or secrets manager instead of committing the value to appsettings. Shell-based hosts use the shell feature path, for example `CShells__Shells__Default__Features__Identity__SigningKey`.

## Build a container image

Build the reference server image from the repository root:

```bash
docker build -f docker/ElsaServer.Dockerfile -t flamme-server:local .
```

This command builds an image; running the reference host also requires PostgreSQL connectivity and environment configuration. Follow the canonical [local setup in the README](../../README.md) to start PostgreSQL and run the host. For containers, configure `ConnectionStrings__PostgreSql` with a database address reachable from the container rather than relying on the host's `localhost`.

Dockerfiles run on .NET 11 by default. Pass `--build-arg DOTNET_RUNTIME_VERSION=10.0` to build a .NET 10 image.

UI work is secondary. [Elsa Studio](https://github.com/elsa-workflows/elsa-studio) is a separate upstream project and is not hosted by this repository.

Production containers must inject a secure JWT signing key through environment variables or a secrets manager. The appsettings placeholder and known public sample keys are rejected during startup outside `Development` or `Demo`.

Default development login is available only when a development configuration explicitly provisions it:

```text
Username: admin
Password: password
```

Do not use development credentials in production.

### TLS and custom certificate authorities

The server image includes the operating system CA bundle for outbound HTTPS.
To trust a private CA, mount a certificate file or directory and set
`EXTRA_CA_CERT` to its path inside the container. The [entrypoint](../../docker/entrypoint.sh)
copies `.crt` and `.pem` files into the system trust directory and refreshes it
before starting the application. This requires permission to write that store.

For example, add these options to your configured container invocation:

```bash
--mount type=bind,source=/path/to/company-ca.crt,target=/certs/company-ca.crt,readonly \
--env EXTRA_CA_CERT=/certs/company-ca.crt
```

If the system trust store cannot be changed, mount a prepared CA bundle and
configure `SSL_CERT_FILE` or `SSL_CERT_DIR` for clients that honor those variables.

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

See [Program.cs](../../src/apps/Elsa.Server.Web/Program.cs).

## Operational Endpoints

With default route prefix `elsa/api`, runtime admin endpoints include:

- `GET /elsa/api/admin/workflow-runtime/status`: requires `read:workflow-runtime`; `ManageWorkflowRuntime` is also accepted for backward compatibility.
- `POST /elsa/api/admin/workflow-runtime/pause`: requires `ManageWorkflowRuntime`.
- `POST /elsa/api/admin/workflow-runtime/resume`: requires `ManageWorkflowRuntime`.
- `POST /elsa/api/admin/workflow-runtime/force-drain`: requires `ManageWorkflowRuntime`.

Custom structured-log, console-log, and OpenTelemetry ingestion/query endpoints and SignalR hubs have been removed. Hosts configure official OpenTelemetry exporters and external logging infrastructure; see the [telemetry configuration and migration guide](opentelemetry-workflows.md).

Health checks are mapped to `/` and `/health/live` for process liveness and `/health/ready` for Elsa runtime readiness in the reference server. See [Health Checks](health-checks.md) for Kubernetes liveness/readiness recommendations and Elsa-specific runtime readiness probes.

## Runtime Knobs

Common runtime-related options in the reference host:

- `RuntimeOptions.InactivityThreshold`
- `BookmarkQueuePurgeOptions.Ttl`
- `CachingOptions.CacheDuration`
- `IncidentOptions.DefaultIncidentStrategy`
- recurring task schedules for trigger queue, bookmark queue purge, and interrupted workflow restart

Configure operational telemetry through standard `OTEL_*` settings and your external observability platform. The former Elsa log capacity, query, heartbeat, redaction, and storage options have been removed.

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

Because source libraries multi-target .NET 10 and .NET 11, package upgrades should be checked against all target frameworks and provider packages. Persistence changes usually need extra scrutiny because each provider package may need migrations or compatibility updates.
