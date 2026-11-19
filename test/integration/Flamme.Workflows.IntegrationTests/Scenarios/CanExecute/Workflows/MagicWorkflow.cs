using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.IntegrationTests.Scenarios.CanExecute.Activities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.CanExecute.Workflows;

public class MagicWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var magicNumberVariable = builder.WithVariable<int>("MagicNumber", default!);

        builder.Root = new Sequence
        {
            Activities =
            {
                new SetVariable<int>(magicNumberVariable, context => context.GetInput<int>("MagicNumber")),
                new WriteLine(context => $"Magic number is {magicNumberVariable.Get(context)}"),
                new CustomActivity(context => magicNumberVariable.Get(context)),
                new WriteLine("Done")
            }
        };
    }
}