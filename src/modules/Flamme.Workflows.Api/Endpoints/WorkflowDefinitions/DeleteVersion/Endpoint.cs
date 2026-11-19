using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Api.Constants;
using Flamme.Workflows.Api.Requirements;
using Flamme.Workflows.Management;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.DeleteVersion;

[PublicAPI]
internal class DeleteVersion(IWorkflowDefinitionManager workflowDefinitionManager, IAuthorizationService authorizationService)
    : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Delete("/workflow-definition-versions/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var authorizationResult = await authorizationService.AuthorizeAsync(User, new NotReadOnlyResource(), AuthorizationPolicies.NotReadOnlyPolicy);

        if (!authorizationResult.Succeeded)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var deleted = await workflowDefinitionManager.DeleteByIdAsync(request.Id, cancellationToken);

        if (!deleted)
            await Send.NotFoundAsync(cancellationToken);
        else
            await Send.NoContentAsync(cancellationToken);
    }
}