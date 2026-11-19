using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Management;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.IsNameUnique;

/// <summary>
/// Checks if a workflow definition name is unique.
/// </summary>
[PublicAPI]
internal class IsNameUnique(IWorkflowDefinitionStore store) : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Get("/workflow-definitions/validation/is-name-unique");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.View);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var isUnique = await store.GetIsNameUnique(request.Name.Trim(), request.DefinitionId, cancellationToken);
        var response = new Response(isUnique);
        
        await Send.OkAsync(response, cancellationToken);
    }
}