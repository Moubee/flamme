using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Models;
using Flamme.Workflows.Api.Constants;
using Flamme.Workflows.Api.Requirements;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Contracts;
using Flamme.Workflows.Management.Filters;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.UpdateReferences;

[PublicAPI]
internal class UpdateReferences(IWorkflowReferenceUpdater workflowReferenceUpdater, IWorkflowDefinitionStore store, IAuthorizationService authorizationService)
    : FlammeEndpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/workflow-definitions/{definitionId}/update-references");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, "publish");
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            DefinitionId = request.DefinitionId,
            VersionOptions = VersionOptions.Latest
        };

        var definition = await store.FindAsync(filter, cancellationToken);

        if (definition == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        var authorizationResult = await authorizationService.AuthorizeAsync(User, new NotReadOnlyResource(definition), AuthorizationPolicies.NotReadOnlyPolicy);

        if (!authorizationResult.Succeeded)
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var result = await workflowReferenceUpdater.UpdateWorkflowReferencesAsync(definition, cancellationToken);
        var affectedWorkflows = result.UpdatedWorkflows;
        var response = new Response(affectedWorkflows.Select(w => w.Name ?? w.DefinitionId));
        await Send.OkAsync(response, cancellationToken);
    }
}