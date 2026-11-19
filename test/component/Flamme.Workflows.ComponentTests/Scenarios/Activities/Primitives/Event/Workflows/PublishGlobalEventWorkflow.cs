using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Primitives.Event.Workflows;

/// <summary>
/// A workflow that publishes a global event with the workflow's correlation ID.
/// </summary>
public class PublishGlobalEventWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder workflow)
    {
        workflow.WithDefinitionId(DefinitionId);
        workflow.Root = new Sequence
        {
            Activities =
            {
                new Start(),
                new PublishEvent
                {
                    EventName = new("GlobalOrderEvent"),
                    CorrelationId = new(context => context.GetWorkflowExecutionContext().CorrelationId),
                    IsLocalEvent = new(false),
                    Payload = new(new OrderEventPayload("Shipped"))
                },
                new End()
            }
        };
    }
}

public sealed record OrderEventPayload(string Status);
