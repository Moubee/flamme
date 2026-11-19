using Flamme.Workflows.Activities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowDispatchNotifications;

public class SimpleWorkflow : WorkflowBase
{
    public const string DefinitionId = "SimpleWorkflow";
    
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.DefinitionId = DefinitionId;
        builder.Root = new WriteLine("Hello from SimpleWorkflow");
    }
}
