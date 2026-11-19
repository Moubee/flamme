using Flamme.Activities.IntegrationTests.Composition.Activities;
using Flamme.Workflows;
using Flamme.Workflows.Activities;

namespace Flamme.Activities.IntegrationTests.Composition.Workflows;

/// <summary>
/// Workflow demonstrating that Complete terminates composite execution immediately.
/// </summary>
public class CompleteTerminatesCompositeWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder workflow)
    {
        workflow.WithDefinitionId(DefinitionId);
        workflow.Root = new Sequence
        {
            Activities =
            {
                new SimpleComposite(),
                new WriteLine("After composite")
            }
        };
    }
}