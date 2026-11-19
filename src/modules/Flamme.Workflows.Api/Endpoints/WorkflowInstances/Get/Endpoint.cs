using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Api.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowInstances.Get;

[PublicAPI]
internal class Get(IWorkflowInstanceStore store) : FlammeEndpoint<Request, WorkflowInstanceModel, WorkflowInstanceMapper>
{
    public override void Configure()
    {
        Get("/workflow-instances/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Instances, CoreVerbs.View);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowInstanceFilter { Id = request.Id };
        var workflowInstance = await store.FindAsync(filter, cancellationToken);

        if (workflowInstance == null)
            await Send.NotFoundAsync(cancellationToken);
        else
            await Send.OkAsync(Map.FromEntity(workflowInstance), cancellationToken);
    }
}