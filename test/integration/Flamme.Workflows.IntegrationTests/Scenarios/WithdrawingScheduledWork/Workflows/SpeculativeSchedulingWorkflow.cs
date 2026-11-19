using Flamme.Workflows.Activities;
using Flamme.Workflows.IntegrationTests.Scenarios.WithdrawingScheduledWork.Activities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WithdrawingScheduledWork.Workflows;

public class SpeculativeSchedulingWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine("Start"),
                new SpeculativeScheduler
                {
                    Speculative = new WriteLine("Withdrawn"),
                    Committed = new WriteLine("Committed")
                },
                new WriteLine("End")
            }
        };
    }
}
