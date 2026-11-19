using Flamme.Common.Models;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities.Flowchart.Extensions;
using Flamme.Workflows.Options;
using Flamme.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.JoinBehaviors;

public class ParallelJoinCompletesTests : IAsyncDisposable
{
    private readonly WorkflowTestFixture _fixture = new(TestContext.Current!.Output.StandardOutput);

    [Test]
    [DisplayName("The ParallelForEach activity completes when its Body contains a Join activity")]
    public async Task Test1()
    {
        // Import workflow.
        var workflowDefinition = await _fixture.ImportWorkflowDefinitionAsync("Scenarios/JoinBehaviors/Workflows/parallel-join.json");

        // Execute with token-based mode.
        var options = new RunWorkflowOptions().WithTokenBasedFlowchart();
        var state = await _fixture.RunWorkflowAsync(workflowDefinition.DefinitionId, options: options);

        // Assert.
        var journal = await _fixture.Services.GetRequiredService<IWorkflowExecutionLogStore>().FindManyAsync(new()
        {
            WorkflowInstanceId = state.Id,
            ActivityId = "70fc1183cd5800f2",
            EventName = "Completed"
        }, PageArgs.All);

        await Assert.That(journal.Items).HasSingleItem();
    }

    public ValueTask DisposeAsync() => TestResourceDisposal.DisposeAsync(_fixture);
}
