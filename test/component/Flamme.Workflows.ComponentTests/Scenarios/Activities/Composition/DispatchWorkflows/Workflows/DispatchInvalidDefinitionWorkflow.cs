using Flamme.Workflows.Activities;
using Flamme.Workflows.IncidentStrategies;
using Flamme.Workflows.Runtime.Activities;
using JetBrains.Annotations;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.DispatchWorkflows.Workflows;

[UsedImplicitly]
public class DispatchInvalidDefinitionWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();
    public static readonly string InvalidChildWorkflowId = "NonExistentWorkflow";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.WorkflowOptions.IncidentStrategyType = typeof(FaultStrategy);

        builder.Root = new Sequence
        {
            Activities =
            {
                new DispatchWorkflow
                {
                    WorkflowDefinitionId = new(InvalidChildWorkflowId),
                    WaitForCompletion = new(true)
                }
            }
        };
    }
}
