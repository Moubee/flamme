using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.Runtime.Responses;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Refresh;

[PublicAPI]
internal class Refresh(IWorkflowDefinitionsRefresher workflowDefinitionsRefresher) : FlammeEndpoint<Request>
{
    private const int BatchSize = 10;

    public override void Configure()
    {
        Post("/actions/workflow-definitions/refresh");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, "refresh");
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var result = await RefreshWorkflowDefinitionsAsync(request.DefinitionIds, cancellationToken);
        if (result.Status == RefreshWorkflowDefinitionsStatus.Completed)
        {
            await Send.OkAsync(new Response(result.Refreshed, result.NotFound), cancellationToken);
        }
        else
        {
            await Send.AcceptedAtAsync<Refresh>(responseBody: new Response(result.Refreshed, result.NotFound), cancellation: cancellationToken);
        }
    }

    private async Task<RefreshWorkflowDefinitionsResponse> RefreshWorkflowDefinitionsAsync(ICollection<string>? definitionIds, CancellationToken cancellationToken)
    {
        var request = new RefreshWorkflowDefinitionsRequest(definitionIds, BatchSize);
        return await workflowDefinitionsRefresher.RefreshWorkflowDefinitionsAsync(request, cancellationToken);
    }
}