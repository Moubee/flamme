using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.ComponentTests.Scenarios.Variables.Activities;

namespace Flamme.Workflows.ComponentTests.Scenarios.Variables.Workflows;

public class CountdownWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        var counter = builder.WithVariable("Counter", 3).WithWorkflowStorage();

        builder.Root = new Sequence
        {
            Activities =
            {
                new While(context => counter.Get(context) > 0)
                {
                    Body = new Sequence
                    {
                        Activities =
                        {
                            new WriteLine(context => $"Counter: {counter.Get(context)}"),
                            new CountdownStep()
                        }
                    }
                }
            }
        };
    }
}