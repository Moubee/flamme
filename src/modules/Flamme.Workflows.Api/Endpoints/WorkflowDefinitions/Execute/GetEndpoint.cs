using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Api.Security;
using Flamme.Workflows.Management;
using Flamme.Workflows.Runtime;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Execute;

/// <summary>
/// An API endpoint that executes a given workflow definition through GET method.
/// </summary>
[PublicAPI]
internal class GetEndpoint(
    IWorkflowDefinitionService workflowDefinitionService,
    IWorkflowRuntime workflowRuntime,
    IWorkflowStarter workflowStarter,
    IApiSerializer apiSerializer,
    WorkflowDefinitionScriptAuthorizationService scriptAuthorizationService)
    : FlammeEndpoint<GetRequest>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Routes("/workflow-definitions/{definitionId}/execute");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.Execute);
        Verbs(FastEndpoints.Http.GET);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(GetRequest request, CancellationToken cancellationToken)
    {
        await WorkflowExecutionHelper.ExecuteWorkflowAsync(
            request,
            workflowDefinitionService,
            workflowRuntime,
            workflowStarter,
            apiSerializer,
            scriptAuthorizationService,
            HttpContext,
            cancellationToken);
    }
}
