using Flamme.Workflows.Activities;
using Flamme.Workflows.IntegrationTests.Scenarios.WithdrawingScheduledWork.Activities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WithdrawingScheduledWork.Workflows;

public class SelfCancellingSchedulingWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine("Start"),
                new SelfCancellingScheduler
                {
                    Child = new WriteLine("Withdrawn")
                },
                new WriteLine("End")
            }
        };
    }
}
