# Repository Map

Elsa Core is organized as a large multi-project .NET solution. The repo favors small, independently packaged modules over one monolith.

## Top-Level Layout

| Path | Purpose |
| --- | --- |
| [src/apps](../../src/apps) | Aspire AppHost, code-first reference server, and shared service defaults. |
| [src/modules](../../src/modules) | Elsa product modules: workflow engine, runtime, management, APIs, HTTP, identity, persistence, diagnostics, scripting, scheduling, tenants, labels, resilience, and more. |
| [src/common](../../src/common) | Shared infrastructure such as feature/module plumbing, mediator, API helpers, and test helpers. |
| [src/clients](../../src/clients) | Client packages, currently including the Elsa API client. |
| [src/extensions](../../src/extensions) | Extension packages that are not core modules. Currently contains [Elsa.Testing.Extensions](../../src/extensions/Elsa.Testing.Extensions). |
| [test/unit](../../test/unit) | Fast unit tests scoped to individual modules or services. |
| [test/integration](../../test/integration) | In-process tests that compose multiple Elsa services. |
| [test/component](../../test/component) | Larger host-level and persistence-oriented scenarios. |
| [test/performance](../../test/performance) | Benchmark and throughput-oriented tests. |
| [Directory.Build.props](../../Directory.Build.props) and [Directory.Build.targets](../../Directory.Build.targets) | Shared MSBuild settings used by the .NET CLI and CI. |
| [doc](../../doc) | ADRs, QA notes, agent logs, bounty docs, and this wiki. |
| [specs](../../specs) | Spec Kit feature specs, plans, tasks, contracts, and quickstarts. |
| [design](../../design) | Logos, screenshots, and visual assets used by public docs and README files. |

## Major Module Families

| Family | Projects | What they own |
| --- | --- | --- |
| Base host package | [Elsa](../../src/modules/Elsa) | `AddElsa`, `ElsaFeature`, default workflow feature wiring. |
| Workflow engine | [Elsa.Workflows.Core](../../src/modules/Elsa.Workflows.Core) | Activities, execution contexts, pipelines, serialization, variables, bookmarks, graphs, flowchart primitives. |
| Workflow management | [Elsa.Workflows.Management](../../src/modules/Elsa.Workflows.Management) | Definitions, instances, stores, import/export, materializers, validation, descriptors. |
| Workflow runtime | [Elsa.Workflows.Runtime](../../src/modules/Elsa.Workflows.Runtime) and [Elsa.Workflows.Runtime.Distributed](../../src/modules/Elsa.Workflows.Runtime.Distributed) | Dispatch, triggers, bookmark queues, runtime logs, background activity scheduling, recovery, distributed runtime support. |
| Alterations | [Elsa.Alterations](../../src/modules/Elsa.Alterations), [Elsa.Alterations.Core](../../src/modules/Elsa.Alterations.Core) | Bulk alteration of running workflow instances: alteration plans, jobs, dispatching, and in-memory stores; PostgreSQL EF Core persistence lives alongside the core. |
| Workflow API | [Elsa.Workflows.Api](../../src/modules/Elsa.Workflows.Api) and [Elsa.Api.Common](../../src/common/Elsa.Api.Common) | FastEndpoints registration, workflow endpoints, real-time workflow updates, API serialization. |
| Expression languages | [Elsa.Expressions](../../src/modules/Elsa.Expressions), [CSharp](../../src/modules/Elsa.Expressions.CSharp), [JavaScript](../../src/modules/Elsa.Expressions.JavaScript), [Python](../../src/modules/Elsa.Expressions.Python), [Liquid](../../src/modules/Elsa.Expressions.Liquid) | Expression evaluation and language-specific activities/descriptors. |
| Transport/activity packages | [Elsa.Http](../../src/modules/Elsa.Http), [Elsa.Scheduling](../../src/modules/Elsa.Scheduling), [Elsa.Resilience](../../src/modules/Elsa.Resilience) | HTTP triggers and calls, scheduled triggers, resilience strategies. |
| Persistence (EF Core) | [Elsa.Persistence.EFCore](../../src/modules/Elsa.Persistence.EFCore) and provider packages under `Elsa.Persistence.EFCore.*` | EF Core stores and provider-specific configuration/migrations. |
| Key-value store | [Elsa.KeyValues](../../src/modules/Elsa.KeyValues) | Generic key-value storage (`IKeyValueStore`) with a default in-memory backing store; used by other modules for ephemeral or cross-request state. |
| Caching | [Elsa.Caching](../../src/modules/Elsa.Caching) | `ICacheManager` and `IChangeTokenSignaler`; provides memory-cache helpers and signal-based cache invalidation used internally by other Elsa modules. |
| Security and tenancy | [Elsa.Identity](../../src/modules/Elsa.Identity), [Elsa.Tenants](../../src/modules/Elsa.Tenants), [Elsa.Tenants.AspNetCore](../../src/modules/Elsa.Tenants.AspNetCore), [Elsa.SasTokens](../../src/modules/Elsa.SasTokens) | Users, applications, roles, API keys, tenants, tenant-aware routing, SAS tokens. |
| External authentication | [Elsa.ExternalAuthentication](../../src/modules/Elsa.ExternalAuthentication), [Elsa.ExternalAuthentication.OpenIdConnect](../../src/modules/Elsa.ExternalAuthentication.OpenIdConnect), [Elsa.ExternalAuthentication.Secrets](../../src/modules/Elsa.ExternalAuthentication.Secrets), and the PostgreSQL EF Core provider package | Server-brokered external identity providers: Identity Provider Connections, OpenID Connect adapter, linked identity resolution, configurable unlinked-identity policies, Elsa credential issuance, and EF Core persistence. See [specs/012-external-authentication/spec.md](../../specs/012-external-authentication/spec.md). |
| Secrets | [Elsa.Secrets](../../src/modules/Elsa.Secrets), [Elsa.Secrets.Persistence.EFCore](../../src/modules/Elsa.Secrets.Persistence.EFCore), [Elsa.Secrets.JavaScript](../../src/modules/Elsa.Secrets.JavaScript) | Named secrets with pluggable stores, extensible secret types (text, RSA key, X.509 certificate), versioning, rotation, revocation, secret resolver, management endpoints, PostgreSQL EF Core persistence, and JavaScript expression access. |
| Workflow telemetry | [WorkflowInstrumentation](../../src/modules/Elsa.Workflows.Core/Telemetry/WorkflowInstrumentation.cs) | `Elsa.Workflows` activity source and meter. Hosts and external observability platforms own collection, export, storage, and viewing; see the [migration guide](opentelemetry-workflows.md). |
| User Tasks | [Elsa.UserTasks](../../src/modules/Elsa.UserTasks), PostgreSQL EF Core persistence (`Elsa.UserTasks.Persistence.EFCore.PostgreSql`) | Identity-neutral durable human-task module: workflow pause/resume for human decisions, a secure task queue, typed outcomes, participant references independent of `Elsa.Identity`. See [specs/013-user-tasks/spec.md](../../specs/013-user-tasks/spec.md). |
| Operational dashboard | [Elsa.Dashboard.Api](../../src/modules/Elsa.Dashboard.Api) | Read-only aggregate endpoints for the Studio operational dashboard: overview, trends, needs-attention findings, recent activity, and workflow hotspots. |
| Application clustering | [Elsa.Hosting.Management](../../src/modules/Elsa.Hosting.Management) | Application instance naming, heartbeat-based cluster membership, and instance-aware hosted service support for multi-node deployments. |
| AI / Weaver | [Elsa.AI.Abstractions](../../src/modules/Elsa.AI.Abstractions), [Elsa.AI.Copilot](../../src/modules/Elsa.AI.Copilot), [Elsa.AI.Host](../../src/modules/Elsa.AI.Host), [Elsa.AI.Persistence.EFCore](../../src/modules/Elsa.AI.Persistence.EFCore) | Weaver AI copilot: streaming chat, context resolution, reviewable proposals, audit, and provider abstraction for AI-assisted workflow authoring. |

## Reference Hosts

- [Elsa.AppHost](../../src/apps/Elsa.AppHost) orchestrates the reference server and PostgreSQL for local development and demonstrations. The root [aspire.config.json](../../aspire.config.json) selects `src/apps/Elsa.AppHost/Elsa.AppHost.csproj`; see the [Aspire development guide](../aspire-development.md).
- [Elsa.Server.Web](../../src/apps/Elsa.Server.Web) is the most useful all-up ASP.NET Core sample. Its [Program.cs](../../src/apps/Elsa.Server.Web/Program.cs) shows typical module composition with identity, PostgreSQL EF Core persistence, runtime, management, HTTP, scheduling, scripting, and multitenancy.
- [Elsa.ServiceDefaults](../../src/apps/Elsa.ServiceDefaults) supplies the reference server's OpenTelemetry configuration for host requests, logs, and workflow execution.

## Build And Package Files

- [Directory.Build.props](../../Directory.Build.props) contains shared MSBuild settings.
- [src/Directory.Build.props](../../src/Directory.Build.props) multi-targets source packages for `net10.0` and `net11.0`.
- [Directory.Packages.props](../../Directory.Packages.props) centrally manages package versions, including framework-specific versions where necessary for .NET 10 and .NET 11.
- [global.json](../../global.json) selects the .NET 11 SDK and Microsoft Testing Platform runner. Build and package commands use the standard .NET CLI; see [Build, Run, And Operate](build-run-operate.md).
- TUnit tests run directly through Microsoft Testing Platform with `dotnet test --solution Elsa.slnx` or `dotnet test --project <path-to-test.csproj>`.

## How To Find Code Fast

Use the feature class first. Modules expose code-first `Features/*Feature.cs` registration. The feature class tells you what the module registers and what other features it depends on. After that, follow contracts and service registrations into implementation files.

Good first searches:

```bash
rg "class .*Feature" src/modules src/common
rg "interface I.*Store" src/modules
rg "AddScoped|AddSingleton|TryAdd" src/modules/Elsa.Workflows.Runtime/Features
rg "Get\\(|Post\\(|Delete\\(" src/modules/Elsa.Workflows.Api/Endpoints
```
