using Flamme.Workflows.Activities;
using Flamme.Workflows.IncidentStrategies;
using JetBrains.Annotations;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.BulkDispatchWorkflows.Workflows;

[UsedImplicitly]
public class FaultingBulkChildWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.WorkflowOptions.IncidentStrategyType = typeof(FaultStrategy);

        builder.Root = new Sequence
        {
            Activities =
            {
                new Fault
                {
                    Message = new("Child workflow failed")
                }
            }
        };
    }
}
