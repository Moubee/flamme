using Flamme.Workflows.Activities;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.Delay.Workflows;

public class DelayWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();
    
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine("Starting...") { Id = "WriteLine1" },
                Flamme.Scheduling.Activities.Delay.FromSeconds(1),
                new WriteLine("End") { Id = "WriteLine2" }
            }
        };
    }
}
