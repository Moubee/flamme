using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Activities.SetOutput;
using JetBrains.Annotations;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.ExecuteWorkflows.Workflows;

[UsedImplicitly]
public class SubroutineWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString(); 
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        var valueInput = builder.WithInput<double>("Value");
        var output = builder.WithOutput<double>("Output");
        
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine(context => $"Running subroutine on value {context.GetInput<double>(valueInput)}..."),
                new SetOutput
                {
                    OutputName = new(output.Name),
                    OutputValue = new(context => context.GetInput<double>(valueInput) * 2)
                }
            }
        };
    }
}