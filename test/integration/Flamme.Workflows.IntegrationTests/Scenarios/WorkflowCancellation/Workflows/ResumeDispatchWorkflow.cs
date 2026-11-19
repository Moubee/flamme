using Flamme.Workflows.Activities;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowCancellation.Workflows;

public class ResumeDispatchWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            {
                new PublishEvent
                {
                    EventName = new Input<string>("ResumeBlockDispatch")
                }
            }
        };
    }
}