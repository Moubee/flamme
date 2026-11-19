using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.IntegrationTests.Scenarios.SetGetVariablesArray;

class SetGetVariableArrayWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder workflow)
    {
        var variable1 = new Variable<string[]>("Variable1", []);
        var currentValueVariable = new Variable<string>("CurrentValue", null!);

        workflow.Root = new Sequence
        {
            Variables =
            {
                variable1
            },

            Activities =
            {
                new SetVariable<string[]>(variable1, ["Line 1", "Line 2"]),
                new ForEach<string>(new Input<ICollection<string>>(variable1))
                {
                    CurrentValue = new Output<string>(currentValueVariable),
                    Body = new WriteLine(currentValueVariable)
                }
            }
        };
    }
}
