namespace Flamme.Workflows.IntegrationTests.Serialization.VariableTypes;

class SampleWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder workflow)
    {
        workflow.WithVariable("Enabled", false);
    }
}