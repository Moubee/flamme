using Elsa.Workflows.State;

namespace Elsa.Workflows.CommitStates;

public interface ICommitStateHandler
{
    Task CommitAsync(WorkflowExecutionContext workflowExecutionContext, CancellationToken cancellationToken = default);
    Task CommitAsync(WorkflowExecutionContext workflowExecutionContext, WorkflowState workflowState, CancellationToken cancellationToken = default);

    /// <summary>Commits the runner's terminal state after the execution pipeline has finished.</summary>
    Task CommitFinalAsync(WorkflowExecutionContext workflowExecutionContext, WorkflowState workflowState, CancellationToken cancellationToken = default) =>
        CommitAsync(workflowExecutionContext, workflowState, cancellationToken);
}
