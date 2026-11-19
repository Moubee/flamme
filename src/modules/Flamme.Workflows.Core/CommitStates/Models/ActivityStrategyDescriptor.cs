namespace Flamme.Workflows.CommitStates;

public record ActivityStrategyDescriptor(string Name, string Description, IActivityCommitStrategy Strategy);