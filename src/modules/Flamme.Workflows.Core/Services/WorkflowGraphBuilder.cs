using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <inheritdoc />
public class WorkflowGraphBuilder(IActivityVisitor activityVisitor, IIdentityGraphService identityGraphService) : IWorkflowGraphBuilder
{
    /// <inheritdoc />
    public async Task<WorkflowGraph> BuildAsync(Workflow workflow, CancellationToken cancellationToken = default)
    {
        var graph = await activityVisitor.VisitAsync(workflow, cancellationToken);
        var nodes = graph.Flatten().ToList();
        
        await identityGraphService.AssignIdentitiesAsync(nodes);
        return new WorkflowGraph(workflow, graph, nodes);
    }
}