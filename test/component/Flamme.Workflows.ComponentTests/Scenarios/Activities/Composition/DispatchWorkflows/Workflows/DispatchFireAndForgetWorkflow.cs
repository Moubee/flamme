using Flamme.Workflows.ComponentTests.Scenarios.Activities.DispatchWorkflows.Workflows;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.DispatchWorkflows.Workflows;

public class DispatchFireAndForgetWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.Root = new DispatchWorkflow
        {
            WorkflowDefinitionId = new(SlowChildWorkflow.DefinitionId),
            WaitForCompletion = new(false)
        };
    }
}
