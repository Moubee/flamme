# Repository Map

Flamme Core is organized as a large multi-project .NET solution. The repo favors small, independently packaged modules over one monolith.

## Top-Level Layout

| Path | Purpose |
| --- | --- |
| [src/apps](../../src/apps) | Aspire AppHost, code-first reference server, and shared service defaults. |
| [src/modules](../../src/modules) | Flamme product modules: workflow engine, runtime, management, APIs, HTTP, identity, persistence, diagnostics, scripting, scheduling, tenants, labels, resilience, and more. |
| [src/common](../../src/common) | Shared infrastructure such as feature/module plumbing, mediator, API helpers, and test helpers. |
| [src/clients](../../src/clients) | Client packages, currently including the Flamme API client. |
| [src/extensions](../../src/extensions) | Extension packages that are not core modules. Currently contains [Flamme.Testing.Extensions](../../src/extensions/Flamme.Testing.Extensions). |
| [test/unit](../../test/unit) | Fast unit tests scoped to individual modules or services. |
| [test/integration](../../test/integration) | In-process tests that compose multiple Flamme services. |
| [test/component](../../test/component) | Larger host-level and persistence-oriented scenarios. |
| [test/performance](../../test/performance) | Benchmark and throughput-oriented tests. |
| [Directory.Build.props](../../Directory.Build.props) and [Directory.Build.targets](../../Directory.Build.targets) | Shared MSBuild settings used by the .NET CLI and CI. |
| [doc](../../doc) | Architecture decisions, migration guides, testing guidance, and this wiki. |

## Major Module Families

| Family | Projects | What they own |
| --- | --- | --- |
| Base host package | [Flamme](../../src/modules/Flamme) | `AddFlamme`, `FlammeFeature`, default workflow feature wiring. |
| Workflow engine | [Flamme.Workflows.Core](../../src/modules/Flamme.Workflows.Core) | Activities, execution contexts, pipelines, serialization, variables, bookmarks, graphs, flowchart primitives. |
| Workflow management | [Flamme.Workflows.Management](../../src/modules/Flamme.Workflows.Management) | Definitions, instances, stores, import/export, materializers, validation, descriptors. |
| Workflow runtime | [Flamme.Workflows.Runtime](../../src/modules/Flamme.Workflows.Runtime) and [Flamme.Workflows.Runtime.Distributed](../../src/modules/Flamme.Workflows.Runtime.Distributed) | Dispatch, triggers, bookmark queues, runtime logs, background activity scheduling, recovery, distributed runtime support. |
| Alterations | [Flamme.Alterations](../../src/modules/Flamme.Alterations), [Flamme.Alterations.Core](../../src/modules/Flamme.Alterations.Core) | Bulk alteration of running workflow instances: alteration plans, jobs, dispatching, and in-memory stores; PostgreSQL EF Core persistence lives alongside the core. |
| Workflow API | [Flamme.Workflows.Api](../../src/modules/Flamme.Workflows.Api) and [Flamme.Api.Common](../../src/common/Flamme.Api.Common) | FastEndpoints registration, workflow endpoints, real-time workflow updates, API serialization. |
| Expression languages | [Flamme.Expressions](../../src/modules/Flamme.Expressions), [CSharp](../../src/modules/Flamme.Expressions.CSharp), [JavaScript](../../src/modules/Flamme.Expressions.JavaScript), [Python](../../src/modules/Flamme.Expressions.Python), [Liquid](../../src/modules/Flamme.Expressions.Liquid) | Expression evaluation and language-specific activities/descriptors. |
| Transport/activity packages | [Flamme.Http](../../src/modules/Flamme.Http), [Flamme.Scheduling](../../src/modules/Flamme.Scheduling), [Flamme.Resilience](../../src/modules/Flamme.Resilience) | HTTP triggers and calls, scheduled triggers, resilience strategies. |
| Persistence (EF Core) | [Flamme.Persistence.EFCore](../../src/modules/Flamme.Persistence.EFCore) and provider packages under `Flamme.Persistence.EFCore.*` | EF Core stores and provider-specific configuration/migrations. |
| Key-value store | [Flamme.KeyValues](../../src/modules/Flamme.KeyValues) | Generic key-value storage (`IKeyValueStore`) with a default in-memory backing store; used by other modules for ephemeral or cross-request state. |
| Caching | [Flamme.Caching](../../src/modules/Flamme.Caching) | `ICacheManager` and `IChangeTokenSignaler`; provides memory-cache helpers and signal-based cache invalidation used internally by other Flamme modules. |
| Security and tenancy | [Flamme.Identity](../../src/modules/Flamme.Identity), [Flamme.Tenants](../../src/modules/Flamme.Tenants), [Flamme.Tenants.AspNetCore](../../src/modules/Flamme.Tenants.AspNetCore), [Flamme.SasTokens](../../src/modules/Flamme.SasTokens) | Users, applications, roles, API keys, tenants, tenant-aware routing, SAS tokens. |
| External authentication | [Flamme.ExternalAuthentication](../../src/modules/Flamme.ExternalAuthentication), [Flamme.ExternalAuthentication.OpenIdConnect](../../src/modules/Flamme.ExternalAuthentication.OpenIdConnect), [Flamme.ExternalAuthentication.Secrets](../../src/modules/Flamme.ExternalAuthentication.Secrets), and the PostgreSQL EF Core provider package | Server-brokered external identity providers: Identity Provider Connections, OpenID Connect adapter, linked identity resolution, configurable unlinked-identity policies, Flamme credential issuance, and EF Core persistence. See the [module guide](../../src/modules/Flamme.ExternalAuthentication/README.md). |
| Secrets | [Flamme.Secrets](../../src/modules/Flamme.Secrets), [Flamme.Secrets.Persistence.EFCore](../../src/modules/Flamme.Secrets.Persistence.EFCore), [Flamme.Secrets.JavaScript](../../src/modules/Flamme.Secrets.JavaScript) | Named secrets with pluggable stores, extensible secret types (text, RSA key, X.509 certificate), versioning, rotation, revocation, secret resolver, management endpoints, PostgreSQL EF Core persistence, and JavaScript expression access. |
| Workflow telemetry | [WorkflowInstrumentation](../../src/modules/Flamme.Workflows.Core/Telemetry/WorkflowInstrumentation.cs) | `Elsa.Workflows` activity source and meter. Hosts and external observability platforms own collection, export, storage, and viewing; see the [migration guide](opentelemetry-workflows.md). |
| User Tasks | [Flamme.UserTasks](../../src/modules/Flamme.UserTasks), PostgreSQL EF Core persistence (`Flamme.UserTasks.Persistence.EFCore.PostgreSql`) | Identity-neutral durable human-task module: workflow pause/resume for human decisions, a secure task queue, typed outcomes, participant references independent of `Flamme.Identity`. See the [User Tasks guide](../user-tasks.md). |
| Operational dashboard | [Flamme.Dashboard.Api](../../src/modules/Flamme.Dashboard.Api) | Read-only aggregate endpoints for the Studio operational dashboard: overview, trends, needs-attention findings, recent activity, and workflow hotspots. |
| Application clustering | [Flamme.Hosting.Management](../../src/modules/Flamme.Hosting.Management) | Application instance naming, heartbeat-based cluster membership, and instance-aware hosted service support for multi-node deployments. |
| AI / Weaver | [Flamme.AI.Abstractions](../../src/modules/Flamme.AI.Abstractions), [Flamme.AI.Copilot](../../src/modules/Flamme.AI.Copilot), [Flamme.AI.Host](../../src/modules/Flamme.AI.Host), [Flamme.AI.Persistence.EFCore](../../src/modules/Flamme.AI.Persistence.EFCore) | Weaver AI copilot: streaming chat, context resolution, reviewable proposals, audit, and provider abstraction for AI-assisted workflow authoring. |

## Reference Hosts

- [Flamme.AppHost](../../src/apps/Flamme.AppHost) orchestrates the reference server and PostgreSQL for local development and demonstrations. The root [aspire.config.json](../../aspire.config.json) selects `src/apps/Flamme.AppHost/Flamme.AppHost.csproj`; see the [Aspire development guide](../aspire-development.md).
- [Flamme.Server.Web](../../src/apps/Flamme.Server.Web) is the most useful all-up ASP.NET Core sample. Its [Program.cs](../../src/apps/Flamme.Server.Web/Program.cs) shows typical module composition with identity, PostgreSQL EF Core persistence, runtime, management, HTTP, scheduling, scripting, and multitenancy.
- [Flamme.ServiceDefaults](../../src/apps/Flamme.ServiceDefaults) supplies the reference server's OpenTelemetry configuration for host requests, logs, and workflow execution.

## Build And Package Files

- [Directory.Build.props](../../Directory.Build.props) contains shared MSBuild settings.
- [src/Directory.Build.props](../../src/Directory.Build.props) multi-targets source packages for `net10.0` and `net11.0`.
- [Directory.Packages.props](../../Directory.Packages.props) centrally manages package versions, including framework-specific versions where necessary for .NET 10 and .NET 11.
- [global.json](../../global.json) selects the .NET 11 SDK and Microsoft Testing Platform runner. Build and package commands use the standard .NET CLI; see [Build, Run, And Operate](build-run-operate.md).
- TUnit tests run directly through Microsoft Testing Platform with `dotnet test --solution Flamme.slnx` or `dotnet test --project <path-to-test.csproj>`.

## How To Find Code Fast

Use the feature class first. Modules expose code-first `Features/*Feature.cs` registration. The feature class tells you what the module registers and what other features it depends on. After that, follow contracts and service registrations into implementation files.

Good first searches:

```bash
rg "class .*Feature" src/modules src/common
rg "interface I.*Store" src/modules
rg "AddScoped|AddSingleton|TryAdd" src/modules/Flamme.Workflows.Runtime/Features
rg "Get\\(|Post\\(|Delete\\(" src/modules/Flamme.Workflows.Api/Endpoints
```
