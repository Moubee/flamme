using Flamme.Workflows.Activities;
using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <summary>
/// Builds a workflow graph from a workflow.
/// </summary>
public interface IWorkflowGraphBuilder
{
    /// <summary>
    /// Builds a workflow graph from a workflow.
    /// </summary>
    Task<WorkflowGraph> BuildAsync(Workflow workflow, CancellationToken cancellationToken = default);
}