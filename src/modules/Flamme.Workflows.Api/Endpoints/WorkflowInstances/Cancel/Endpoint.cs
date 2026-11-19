using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Workflows.Api.Endpoints.WorkflowInstances.Cancel;

[PublicAPI]
internal class Cancel(IWorkflowCancellationService workflowCancellationService)
    : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Post("/cancel/workflow-instances/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Instances, "cancel");
        //Allows for post with empty body
        Description(x => x.Accepts<Request>("*/*"), clearDefaults: true);
    }
    
    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        await workflowCancellationService.CancelWorkflowAsync(request.Id, cancellationToken);
        
        await Send.OkAsync(cancellation: cancellationToken);
    }
}