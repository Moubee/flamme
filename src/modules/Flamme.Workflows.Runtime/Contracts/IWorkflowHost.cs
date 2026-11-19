using Flamme.Workflows.Activities;
using Flamme.Workflows.Models;
using Flamme.Workflows.Options;
using Flamme.Workflows.State;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// Represents a single workflow instance that can be executed and takes care of publishing various lifecycle events.
/// </summary>
[Obsolete("Use IWorkflowRuntime and IWorkflowClient services instead.")]
public interface IWorkflowHost
{
    /// <summary>
    /// The workflow graph.
    /// </summary>
    WorkflowGraph WorkflowGraph { get; }
    
    /// <summary>
    /// The workflow.
    /// </summary>
    Workflow Workflow { get; }

    /// <summary>
    /// Gets or sets the workflow state.
    /// </summary>
    WorkflowState WorkflowState { get; set; }
    
    /// <summary>
    /// Gets the last result of the workflow execution, if any.
    /// </summary>
    object? LastResult { get; }

    /// <summary>
    /// Returns a value indicating whether the specified workflow can start a new instance or not.
    /// </summary>
    Task<bool> CanStartWorkflowAsync(RunWorkflowOptions? @params = default, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Starts a new workflow instance.
    /// </summary>
    Task<RunWorkflowResult> RunWorkflowAsync(RunWorkflowOptions? @params = default, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cancels the workflow instance.
    /// </summary>
    Task CancelWorkflowAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Persists the workflow state.
    /// </summary>
    Task PersistStateAsync(CancellationToken cancellationToken = default);
}