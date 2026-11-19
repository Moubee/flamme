using Flamme.Workflows.Activities;
using Flamme.Workflows.Activities.Flowchart.Activities;
using Flamme.Workflows.Activities.Flowchart.Models;

namespace Flamme.Workflows.IntegrationTests.Scenarios.JoinBehaviors.Workflows;

public class ImplicitLoopWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var counterVariable = builder.WithVariable("Counter", 0);
        var start = new WriteLine("Start");
        var incrementCounter = new SetVariable<int>(counterVariable, context => counterVariable.Get(context) + 1);
        var counterGreaterThanOne = new FlowDecision(context => counterVariable.Get(context) > 1);
        var retry = new WriteLine("Retry");
        var end = new WriteLine("End");

        builder.Root = new Flowchart
        {
            Start = start,
            
            Activities =
            {
                start,
                incrementCounter,
                counterGreaterThanOne,
                retry,
                end
            },
            
            Connections =
            {
                new(start, incrementCounter),
                new(incrementCounter, counterGreaterThanOne),
                new(new(counterGreaterThanOne, "False"), new Endpoint(retry)),
                new(new(counterGreaterThanOne, "True"), new Endpoint(end)),
                new(retry, incrementCounter),
            }
        };
    }
}