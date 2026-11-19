namespace Flamme.Workflows.CommitStates;

public interface IWorkflowCommitStrategy
{
    CommitAction ShouldCommit(WorkflowCommitStateStrategyContext context);
}