using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Extensions;
using Flamme.Workflows.Management;
using Flamme.Workflows.Serialization.Converters;
using Flamme.Workflows.Serialization.Helpers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Graph;

[PublicAPI]
internal class Graph(IWorkflowDefinitionService workflowDefinitionService, IApiSerializer apiSerializer, ActivityWriter activityWriter) : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Get("/workflow-definitions/subgraph/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.View);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var workflowGraph = await workflowDefinitionService.FindWorkflowGraphAsync(request.Id, cancellationToken);

        if (workflowGraph == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        var parentNode = workflowGraph.NodeIdLookup.TryGetValue(request.ParentNodeId, out var node) ? node : workflowGraph.Root;
        var serializerOptions = apiSerializer.GetOptions().Clone();
        serializerOptions.Converters.Add(new RootActivityNodeConverter(activityWriter));
        await HttpContext.Response.WriteAsJsonAsync(parentNode, serializerOptions, cancellationToken);
    }
}