# Flamme

**A .NET process orchestrator focused on performance and distributed production workloads.**

[![Build and packages](https://github.com/DenDeline/flamme/actions/workflows/packages.yml/badge.svg)](https://github.com/DenDeline/flamme/actions/workflows/packages.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Flamme is an independent fork of [Elsa Workflows](https://github.com/elsa-workflows/elsa-core), based on [Elsa 3.9](https://github.com/elsa-workflows/elsa-core/releases/tag/3.9.0). The maintainer selected 3.9 as the most stable baseline for this fork. The **Flamme** name marks a focused direction: orchestrating processes under high load, improving runtime performance, and operating across distributed production environments, with **Kubernetes as the primary hosting target**.

> [!IMPORTANT]
> These are Flamme's engineering priorities. They do not establish a throughput guarantee or mean that the reference server is a production deployment template. See the [project direction](doc/project-direction.md) and [roadmap](ROADMAP.md) for the distinction between current foundations and planned work.

[Why Flamme?](#why-flamme) · [Scope](#scope) · [Run locally](#run-locally) · [Kubernetes](#kubernetes) · [Documentation](#documentation) · [Contributing](#contributing)

## Why Flamme?

Elsa provides the workflow engine foundation: activities, workflow definitions, persistence, execution, and APIs. Flamme builds on that work with a narrower scope so engineering effort can concentrate on process orchestration, performance, and production operations.

| Priority | Direction |
| --- | --- |
| **Process orchestration** | Coordinate services and long-running business processes through code and APIs. |
| **High-load performance** | Measure throughput, latency, resource use, and contention; use reproducible workloads to guide improvements. |
| **Distributed operation** | Prioritize coordination, recovery, graceful shutdown, and observable behavior across runtime instances. |
| **Kubernetes hosting** | Make container lifecycle, readiness, deployment, and operational guidance central to development. |
| **Focused persistence** | Support PostgreSQL as the only durable database today to reduce implementation and validation scope. |

The backend and runtime take priority. UI development is currently secondary; this repository does not host a designer. [Elsa Studio](https://github.com/elsa-workflows/elsa-studio) remains a separate upstream project, and compatibility with Flamme changes should be evaluated for your use case.

Flamme retains identifiers such as `Elsa.*`, `AddElsa(...)`, `Elsa.slnx`, and `/elsa/api` in the codebase. The project rename does not by itself rename packages, namespaces, configuration keys, or API routes. See the [original repository](https://github.com/elsa-workflows/elsa-core) for upstream development and attribution.

## Scope

Current foundations include C# and JSON workflow definitions, composable activities, short- and long-running execution, triggers and bookmarks, HTTP APIs, scheduling, distributed runtime support, and diagnostics. The [repository wiki](doc/wiki/README.md) maps these capabilities to their source modules.

### Persistence

| Backend | Status |
| --- | --- |
| **PostgreSQL** | The only supported durable database, through the existing EF Core providers. |
| **In-memory stores** | Available for development and tests. State is process-local, lost at restart, and not shared between nodes. |
| **MySQL, SQLite, Cassandra** | Planned future work; currently unsupported. Cassandra is planned for high-load scenarios. |

Other database providers and the experimental persistence vNext system have been removed. Follow the [PostgreSQL migration guide](doc/postgres-inmemory-migration.md) when moving an existing host. Durable operational logs require separate consideration: no PostgreSQL structured-log backend is currently bundled.

### .NET tooling

The repository uses the **.NET SDK selected by [global.json](global.json)**: 11.0.100 with patch roll-forward. Libraries, app hosts, and correctness tests target **.NET 10 and .NET 11**; performance benchmarks target .NET 10. Tests use **Microsoft Testing Platform and TUnit**. Install the .NET 10 runtime alongside the selected SDK to run both supported targets.

## Run locally

Use the reference server to explore the backend. The commands below use a local PostgreSQL container and run the application on the host. Run them from the repository root with the selected .NET SDK and Docker installed, and port 5432 available locally.

1. Start the development database:

   ```bash
   docker run --detach --name flamme-postgres \
     --publish 127.0.0.1:5432:5432 \
     --env POSTGRES_USER=elsa \
     --env POSTGRES_PASSWORD=elsa \
     --env POSTGRES_DB=elsa \
     --volume flamme-postgres-17-data:/var/lib/postgresql/data \
     postgres:17-alpine
   ```

   This example uses a separate development volume and pins the PostgreSQL major version to match its data-directory layout. On later runs, use `docker start flamme-postgres` to reuse the container and its data.

2. Repeat the readiness check until PostgreSQL reports that it is accepting connections:

   ```bash
   docker exec flamme-postgres pg_isready -h localhost -U elsa -d elsa
   ```

3. Run the server using the checked-in local database settings:

   ```bash
   ASPNETCORE_ENVIRONMENT=Development \
   Http__BaseUrl=http://localhost:5001 \
   Scripting__Python__AllowHostCodeExecution=false \
   dotnet run --project src/apps/Elsa.Server.Web/Elsa.Server.Web.csproj \
     --framework net11.0 --no-launch-profile --urls http://localhost:5001
   ```

   This shell example disables Python execution so the quickstart does not require a native Python runtime. The [scripting guide](doc/wiki/expressions-and-scripting.md#python) describes enabling it.

| Endpoint | Purpose |
| --- | --- |
| [`/health/live`](http://localhost:5001/health/live) | Process liveness. |
| [`/health/ready`](http://localhost:5001/health/ready) | Runtime readiness, including configured dependency checks. |
| [`/elsa/api`](http://localhost:5001/elsa/api) | Management API route prefix; individual endpoints require authentication and authorization. |

The Development configuration contains sample identities and a development signing key. Configure your own credentials, JWT signing key, and database connection for other environments; see the [server configuration notes](src/apps/Elsa.Server.Web/README.md). The local sample permits single-host locks for development.

Build the solution with:

```bash
dotnet build Elsa.slnx --configuration Release
```

See [Build, Run, And Operate](doc/wiki/build-run-operate.md) for container builds and [CONTRIBUTING.md](CONTRIBUTING.md) for targeted builds, tests, and packaging.

## Kubernetes

Kubernetes is Flamme's primary hosting target. Production hardening and deployment guidance are active roadmap priorities; the reference application and local Compose services are development examples.

Use separate liveness and readiness probes, configure PostgreSQL persistence, and provide a distributed lock provider appropriate for your cluster. The reference server's local file-system locks do not coordinate pods with separate file systems. Start with the [health-check guidance](doc/wiki/health-checks.md#kubernetes-recommendation) and [distributed runtime notes](doc/wiki/workflow-runtime.md#distributed-lock-provider-safety), then consult the [roadmap](ROADMAP.md) for planned operational work.

## Documentation

| Resource | Purpose |
| --- | --- |
| [Project direction](doc/project-direction.md) | Fork rationale, scope, and relationship to upstream. |
| [Roadmap](ROADMAP.md) | Flamme's development priorities and future database plans. |
| [Repository wiki](doc/wiki/README.md) | Architecture, modules, runtime behavior, and source landmarks. |
| [Persistence migration](doc/postgres-inmemory-migration.md) | Removed providers and PostgreSQL configuration. |
| [Server configuration](src/apps/Elsa.Server.Web/README.md) | Reference host settings, identity, and ingress rate limiting. |
| [Upstream Elsa documentation](https://docs.elsaworkflows.io/) | Background on inherited concepts; verify differences against Flamme's code and documentation. |

## Contributing

Use [Flamme issues](https://github.com/DenDeline/flamme/issues) for bug reports, performance evidence, and proposals. Contributions should align with the [project direction](doc/project-direction.md); include representative workloads and measurements when proposing performance changes. Follow the [contribution guide](CONTRIBUTING.md) for the development workflow and validation commands.

Report suspected vulnerabilities privately using the [security policy](SECURITY.md).

## License and attribution

Flamme is licensed under the [MIT License](LICENSE) and retains the original copyright notice. Thanks to the [Elsa Workflows contributors](https://github.com/elsa-workflows/elsa-core) for the foundation on which this fork builds.
