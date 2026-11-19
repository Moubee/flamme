using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Api.Constants;
using Flamme.Workflows.Api.Requirements;
using Flamme.Workflows.Management;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.BulkDeleteVersions;

[PublicAPI]
internal class BulkDeleteVersions(IWorkflowDefinitionManager workflowDefinitionManager, IAuthorizationService authorizationService)
    : FlammeEndpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/bulk-actions/delete/workflow-definitions/by-id");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.Delete);
    }

    public override async Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken)
    {
        var authorizationResult = await authorizationService.AuthorizeAsync(User, new NotReadOnlyResource(), AuthorizationPolicies.NotReadOnlyPolicy);

        if (!authorizationResult.Succeeded)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return null!;
        }

        var count = await workflowDefinitionManager.BulkDeleteByIdsAsync(request.Ids, cancellationToken);
        return new Response(count);
    }
}