using Flamme.Activities.IntegrationTests.Composition.Activities;
using Flamme.Workflows;
using Flamme.Workflows.Activities;

namespace Flamme.Activities.IntegrationTests.Composition.Workflows;

/// <summary>
/// Workflow with nested composite activities to verify Complete only affects immediate parent.
/// </summary>
public class CompleteInNestedCompositeWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder workflow)
    {
        workflow.WithDefinitionId(DefinitionId);
        workflow.Root = new Sequence
        {
            Activities =
            {
                new WriteLine("Outer composite started"),
                new OuterComposite(),
                new WriteLine("Outer composite completed")
            }
        };
    }
}