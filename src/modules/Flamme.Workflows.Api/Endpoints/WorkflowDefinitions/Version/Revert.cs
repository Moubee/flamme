using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Models;
using Flamme.Workflows.Api.Constants;
using Flamme.Workflows.Api.Requirements;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Models;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Version;

[PublicAPI]
internal class RevertVersion(IWorkflowDefinitionManager workflowDefinitionManager, IAuthorizationService authorizationService, IWorkflowDefinitionStore store)
    : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Post("workflow-definitions/{definitionId}/revert/{version}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.DefinitionVersions, "revert");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var definitionId = Route<string>("definitionId")!;
        var version = Route<int>("version");

        var filter = new WorkflowDefinitionFilter
        {
            DefinitionId = definitionId,
            VersionOptions = VersionOptions.SpecificVersion(version)
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

        var newDefinition = await workflowDefinitionManager.RevertVersionAsync(definitionId, version, cancellationToken);
        var newDefinitionSummary = WorkflowDefinitionSummary.FromDefinition(newDefinition);

        await Send.CreatedAtAsync("GetWorkflowDefinitionById", new
        {
            id = newDefinition.Id
        }, newDefinitionSummary, cancellation: cancellationToken);
    }
}