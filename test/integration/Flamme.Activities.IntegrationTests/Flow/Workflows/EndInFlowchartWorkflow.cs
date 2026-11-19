using Flamme.Workflows;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Activities.Flowchart.Activities;
using Flamme.Workflows.Activities.Flowchart.Models;

namespace Flamme.Activities.IntegrationTests.Flow.Workflows;

/// <summary>
/// Workflow demonstrating that End terminates a flowchart.
/// </summary>
public class EndInFlowchartWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder workflow)
    {
        workflow.WithDefinitionId(DefinitionId);

        var start = new WriteLine("Start");
        var pathA = new WriteLine("Path A executed");
        var end = new End();
        var pathB = new WriteLine("Path B executed");
        var afterFlowchart = new WriteLine("After flowchart");

        workflow.Root = new Sequence
        {
            Activities =
            {
                new Flowchart
                {
                    Start = start,
                    Activities =
                    {
                        start,
                        pathA,
                        end,
                        pathB
                    },
                    Connections =
                    {
                        new Connection(start, pathA),
                        new Connection(pathA, end),
                        new Connection(end, pathB)
                    }
                },
                afterFlowchart
            }
        };
    }
}
