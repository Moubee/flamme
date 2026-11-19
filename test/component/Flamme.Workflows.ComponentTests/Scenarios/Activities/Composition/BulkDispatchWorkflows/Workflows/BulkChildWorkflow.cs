using Flamme.Extensions;
using Flamme.Workflows.Activities;
using JetBrains.Annotations;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.BulkDispatchWorkflows.Workflows;

[UsedImplicitly]
public class BulkChildWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        var item = builder.WithInput<string>("Item");

        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine(context => $"Processing item: {context.GetInput<string>(item)}")
            }
        };
    }
}
