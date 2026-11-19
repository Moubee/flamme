using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Extensions;
using Flamme.Workflows.Management;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowInstances.ExecutionState;

/// <summary>
/// Returns the execution state of the specified workflow instance.
/// </summary>
[PublicAPI]
internal class ExecutionState(IWorkflowInstanceStore store) : FlammeEndpoint<Request, Response>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/workflow-instances/{id}/execution-state");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Instances, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var workflowInstance = await store.FindAsync(request.WorkflowInstanceId, cancellationToken);

        if (workflowInstance == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        var response = new Response(
            workflowInstance.Status,
            workflowInstance.SubStatus,
            workflowInstance.UpdatedAt);

        await Send.OkAsync(response, cancellationToken);
    }
}