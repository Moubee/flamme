using Flamme.Extensions;
using Flamme.Workflows.Activities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowInstanceName.Workflows;

public class NamedWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new WriteLine(x => x.GetWorkflowExecutionContext().Name);
    }
}