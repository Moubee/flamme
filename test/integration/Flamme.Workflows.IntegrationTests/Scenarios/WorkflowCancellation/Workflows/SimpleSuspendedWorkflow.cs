using Flamme.Workflows.Activities;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowCancellation.Workflows;

public class SimpleSuspendedWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            {
                new Start(),
                new Event("BlockingEvent"),
                new WriteLine("Workflow was not properly blocked")
            },
        };
    }
}