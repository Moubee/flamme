using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Management;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Count;

/// <summary>
/// An endpoint for counting workflow definitions.
/// </summary>
[PublicAPI]
internal class Count(IWorkflowDefinitionStore store) : FlammeEndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Get("/workflow-definitions/query/count");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var count = await store.CountDistinctAsync(cancellationToken);
        var response = new Response(count);
        await Send.OkAsync(response, cancellationToken);
    }
}