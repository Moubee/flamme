using Flamme.Workflows.Activities.Flowchart.Activities;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Branching.FlowJoin;

public class SingleJoinWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Flowchart
        {
            Activities =
            {
                new Workflows.Activities.Flowchart.Activities.FlowJoin()
            }
        };
    }
}