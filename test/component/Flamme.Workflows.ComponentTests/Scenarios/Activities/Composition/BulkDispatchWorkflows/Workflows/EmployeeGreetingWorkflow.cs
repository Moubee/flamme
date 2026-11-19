using Flamme.Extensions;
using Flamme.Workflows.Activities;
using JetBrains.Annotations;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.BulkDispatchWorkflows.Workflows;

[UsedImplicitly]
public class EmployeeGreetingWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        var employeeName = builder.WithInput<string>("Employee");
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine(x => $"Hello {x.GetInput<string>(employeeName)}")
            }
        };
    }
}