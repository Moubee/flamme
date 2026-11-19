using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Activities.Flowchart.Activities;
using Flamme.Workflows.Api.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Serialization.Converters;
using Flamme.Workflows.Serialization.Helpers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Graph.Segments;

[PublicAPI]
internal class Nodes(IWorkflowDefinitionService workflowDefinitionService, IApiSerializer apiSerializer, ActivityWriter activityWriter) : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Get("/workflow-definitions/subgraph/segments/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.View);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var workflowGraph = await workflowDefinitionService.FindWorkflowGraphAsync(request.Id, cancellationToken);

        if (workflowGraph?.Root == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        var nodeId = request.ChildNodeId;

        if (!workflowGraph.NodeIdLookup.TryGetValue(nodeId, out var childNode))
        {
            AddError("Unknown node ID");
            await Send.ErrorsAsync(cancellation: cancellationToken);
            return;
        }
        
        var ancestors = childNode.Ancestors().ToList();
        var serializerOptions = apiSerializer.GetOptions().Clone().WithConverters(new RootActivityNodeConverter(activityWriter));
        var segments = new Stack<ActivityPathSegment>();
        var currentNode = ancestors.FirstOrDefault();
        var previousNode = childNode;

        while (currentNode != null)
        {
            if (currentNode.Activity is not Workflow and not Flowchart)
            {
                var currentActivity = currentNode.Activity;
                var currentPort = previousNode.Port;
                var activityName = currentActivity.Name ?? currentActivity.Type;
                var segment = new ActivityPathSegment(currentNode.NodeId, currentActivity.Id, currentActivity.Type, currentPort, activityName);
                segments.Push(segment);
            }

            previousNode = currentNode;
            currentNode = currentNode.Parents.FirstOrDefault();
        }

        var leafSegment = segments.LastOrDefault();
        var container = leafSegment == null ? ancestors.LastOrDefault() ?? childNode : ancestors.Last(x => x.Activity.Id == leafSegment.ActivityId);
        var response = new Response(childNode, container, segments.ToList());
        await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
    }
}