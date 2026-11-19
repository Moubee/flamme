# Workflow API

The workflow API exposes management, runtime, descriptors, execution logs, tasks, installed features, runtime admin, and real-time workflow updates. It uses FastEndpoints with Flamme-specific serializer configuration.

Start in [src/modules/Flamme.Workflows.Api](../../src/modules/Flamme.Workflows.Api) and shared API infrastructure in [src/common/Flamme.Api.Common](../../src/common/Flamme.Api.Common).

## Feature Wiring

[WorkflowsApiFeature](../../src/modules/Flamme.Workflows.Api/Features/WorkflowsApiFeature.cs):

- depends on workflow instances, workflow management, workflow runtime, and SAS tokens
- registers its endpoint assembly with the module
- calls `AddFastEndpointsFromModule()`
- configures API serialization
- registers `IWorkflowDefinitionLinker`
- registers read-only-mode authorization requirement handling
- registers workflow instance export naming

The module extension is [UseWorkflowsApi](../../src/modules/Flamme.Workflows.Api/Extensions/ModuleExtensions.cs).

## Route Prefix

The default route prefix is `elsa/api`, defined in [ApiEndpointOptions](../../src/modules/Flamme.Workflows.Api/Options/ApiEndpointOptions.cs). Endpoint-routed ASP.NET hosts apply it with [MapWorkflowsApi](../../src/common/Flamme.Api.Common/Extensions/WebApplicationExtensions.cs):

```csharp
var routePrefix = app.Services.GetRequiredService<IOptions<ApiEndpointOptions>>().Value.RoutePrefix;
app.MapWorkflowsApi(routePrefix);
```

With the default prefix, endpoint paths look like `/elsa/api/workflow-definitions`.

## FastEndpoints Registration

FastEndpoints assemblies are collected through module properties:

- [AddFastEndpointsAssembly](../../src/common/Flamme.Api.Common/Extensions/ModuleExtensions.cs)
- [AddFastEndpointsFromModule](../../src/common/Flamme.Api.Common/Extensions/ModuleExtensions.cs)

This allows multiple features to contribute endpoints before FastEndpoints is registered.

## Endpoint Categories

| Category | Folder | Examples |
| --- | --- | --- |
| Workflow definitions | [Endpoints/WorkflowDefinitions](../../src/modules/Flamme.Workflows.Api/Endpoints/WorkflowDefinitions) | list, get, post, publish, retract, delete, import, export, dispatch, execute, graph, refresh, reload. |
| Workflow instances | [Endpoints/WorkflowInstances](../../src/modules/Flamme.Workflows.Api/Endpoints/WorkflowInstances) | list, get, delete, cancel, bulk cancel/delete, import/export, execution state, variables, journal. |
| Activity executions | [Endpoints/ActivityExecutions](../../src/modules/Flamme.Workflows.Api/Endpoints/ActivityExecutions) and [ActivityExecutionSummaries](../../src/modules/Flamme.Workflows.Api/Endpoints/ActivityExecutionSummaries) | list, get, count, report, call stack, summaries. |
| Descriptors | [ActivityDescriptors](../../src/modules/Flamme.Workflows.Api/Endpoints/ActivityDescriptors), [VariableTypes](../../src/modules/Flamme.Workflows.Api/Endpoints/VariableTypes), [StorageDrivers](../../src/modules/Flamme.Workflows.Api/Endpoints/StorageDrivers), [IncidentStrategies](../../src/modules/Flamme.Workflows.Api/Endpoints/IncidentStrategies), [CommitStrategies](../../src/modules/Flamme.Workflows.Api/Endpoints/CommitStrategies), [Scripting](../../src/modules/Flamme.Workflows.Api/Endpoints/Scripting) | designer metadata and option providers. |
| Runtime admin | [Endpoints/RuntimeAdmin](../../src/modules/Flamme.Workflows.Api/Endpoints/RuntimeAdmin) | status, pause, resume, force drain. |
| Events and tasks | [Endpoints/Events](../../src/modules/Flamme.Workflows.Api/Endpoints/Events), [Endpoints/Tasks](../../src/modules/Flamme.Workflows.Api/Endpoints/Tasks) | trigger event, complete task. |
| Package and features | [Endpoints/Package](../../src/modules/Flamme.Workflows.Api/Endpoints/Package), [Endpoints/Features](../../src/modules/Flamme.Workflows.Api/Endpoints/Features) | package version and installed feature metadata. |

## Common Endpoint Shape

Endpoint classes typically derive from Flamme API base classes in [Flamme.Api.Common](../../src/common/Flamme.Api.Common). They configure route, verb, permissions, and response shape in `Configure()`, then implement either `ExecuteAsync` or `HandleAsync` depending on the FastEndpoints pattern used by that area of the module.

When adding endpoints:

- keep one endpoint per folder/action
- keep request and response models near the endpoint
- follow the `ExecuteAsync` or `HandleAsync` pattern used by nearby endpoints in the same area
- use `ConfigurePermissions` for protected operations
- use the configured API serializer rather than custom JSON settings
- add route examples to relevant docs when behavior is externally visible

## Real-Time Workflow Updates

Real-time updates live under [RealTime](../../src/modules/Flamme.Workflows.Api/RealTime):

- [WorkflowInstanceHub](../../src/modules/Flamme.Workflows.Api/RealTime/Hubs/WorkflowInstanceHub.cs)
- [BroadcastWorkflowProgress](../../src/modules/Flamme.Workflows.Api/RealTime/Handlers/BroadcastWorkflowProgress.cs)
- client contract [IWorkflowInstanceClient](../../src/modules/Flamme.Workflows.Api/RealTime/Contracts/IWorkflowInstanceClient.cs)
- messages for activity and workflow execution updates

Hosts map these hubs with `app.UseWorkflowsSignalRHubs()` when SignalR is enabled.

`WorkflowInstanceHub` carries `[Authorize]`, so every connection requires authentication. The `ObserveInstanceAsync` method additionally enforces `read:workflow-instances` (or wildcard `read:*` or `*`) and tenant access: callers can only observe workflow instances that belong to their current tenant or are tenant-agnostic.

## Authorization And Read-Only Mode

Workflow API registers [NotReadOnlyRequirementHandler](../../src/modules/Flamme.Workflows.Api/Requirements/NotReadOnlyRequirement.cs) and the policy name in [AuthorizationPolicies](../../src/modules/Flamme.Workflows.Api/Constants/AuthorizationPolicies.cs). Mutable workflow definition endpoints use this policy to honor management read-only mode.

Identity and API key setup are supplied by `Flamme.Identity`; see [Identity, Tenancy, And Security](identity-tenancy-security.md).

## API Client

The generated or hand-maintained client project is [src/clients/Flamme.Api.Client](../../src/clients/Flamme.Api.Client). When endpoint contracts or enums change, check whether the client has mirrored models that need updating. The graceful shutdown plan explicitly called out mirroring `WorkflowSubStatus.Interrupted` in the API client.

## JSON Serialization Errors

Hosts can use [UseJsonSerializationErrorHandler](../../src/modules/Flamme.Workflows.Api/Extensions/ApplicationBuilderExtensions.cs), which installs middleware that returns JSON error responses for serialization failures. The reference server maps it after workflow API endpoints.

## Endpoint Discovery Command

To quickly list routes:

```bash
rg "Get\\(|Post\\(|Delete\\(|Put\\(|Patch\\(|Routes\\(|Verbs\\(" src/modules/Flamme.Workflows.Api/Endpoints -g "Endpoint.cs"
```
