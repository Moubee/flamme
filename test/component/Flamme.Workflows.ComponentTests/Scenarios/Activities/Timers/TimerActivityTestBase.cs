using Flamme.Common.Models;
using Flamme.Testing.Shared.Services;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers;

public abstract class TimerActivityTestBase(App app) : AppComponentTest(app)
{
    protected async Task AssertActivityBlocksAndResumes(string workflowDefinitionId)
    {
        var workflowRunner = Scope.ServiceProvider.GetRequiredService<AsyncWorkflowRunner>();
        var result = await workflowRunner.RunAndAwaitWorkflowCompletionAsync(
            WorkflowDefinitionHandle.ByDefinitionId(workflowDefinitionId, VersionOptions.Published));

        await Assert.That(result.WorkflowExecutionContext.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);

        var writeLineRecords = result.ActivityExecutionRecords
            .Where(x => x.ActivityType == "Elsa.WriteLine")
            .OrderBy(x => x.CompletedAt)
            .ToList();

        await Assert.That(writeLineRecords.Count).IsEqualTo(2);
        await Assert.That(writeLineRecords[0].ActivityId).IsEqualTo("WriteLine1");
        await Assert.That(writeLineRecords[1].ActivityId).IsEqualTo("WriteLine2");
    }
}
