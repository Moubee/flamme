using Flamme.Workflows.Activities;
using Flamme.Workflows.IntegrationTests.Scenarios.SetGetVariablesFromActivities.Activities;
using Flamme.Workflows.Memory;

namespace Flamme.Workflows.IntegrationTests.Scenarios.SetGetVariablesFromActivities.Workflows;

class SampleWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder workflow)
    {
        var variable1 = new Variable<string>("Variable1", "");

        workflow.Root = new Sequence
        {
            Variables =
            {
                variable1
            },

            Activities =
            {
                new CreateVariableActivity(),
                new ReadVariableActivity
                {
                    Result = new(variable1)
                },
                new WriteLine(variable1)
            }
        };
    }
}