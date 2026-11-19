using Flamme.Extensions;
using Flamme.Workflows.Activities;

namespace Flamme.Workflows.IntegrationTests.Serialization.ProgrammaticWorkflowsAndDelegates;

/// <inheritdoc />
public class GreeterWorkflow : WorkflowBase
{
    /// <inheritdoc />
    protected override void Build(IWorkflowBuilder builder)
    {
        var messageInput = builder.WithInput<string>("Message", "The message to write to the console.");
        
        builder.Name = "Greeter Workflow";
        builder.Root = new WriteLine(context => context.GetInput<string>(messageInput));
    }
}