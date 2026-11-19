namespace Flamme.Workflows.CommitStates;

public interface IActivityCommitStrategy
{
    CommitAction ShouldCommit(ActivityCommitStateStrategyContext context);
}