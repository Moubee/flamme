using System.Text.Json;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.ExecuteWorkflows.Workflows;

public class MainWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        var workflowResult = builder.WithVariable<ExecuteWorkflowResult>("WorkflowResult", default!);
        builder.Root = new Sequence
        {
            Activities =
            {
                new ExecuteWorkflow
                {
                    WorkflowDefinitionId = new(SubroutineWorkflow.DefinitionId),
                    Input = new(new Dictionary<string, object>
                    {
                        ["Value"] = 21
                    }),
                    Result = new(workflowResult)
                },
                new WriteLine(context => $"Subroutine output: {JsonSerializer.Serialize(workflowResult.Get(context), TestJsonSerializerContext.Default.ExecuteWorkflowResult)}")
            }
        };
    }
}