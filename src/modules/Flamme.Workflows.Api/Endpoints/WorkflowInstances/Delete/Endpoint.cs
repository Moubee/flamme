using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowInstances.Delete;

[PublicAPI]
internal class Delete(IWorkflowRuntime workflowRuntime) : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Delete("/workflow-instances/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Instances, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var client = await workflowRuntime.CreateClientAsync(request.Id, cancellationToken);
        var deleted = await client.DeleteAsync(cancellationToken);

        if (deleted)
            await Send.NoContentAsync(cancellationToken);
        else
            await Send.NotFoundAsync(cancellationToken);
    }
}