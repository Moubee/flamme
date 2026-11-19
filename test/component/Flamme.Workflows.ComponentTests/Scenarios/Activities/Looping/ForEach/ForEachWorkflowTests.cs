using Flamme.Common.Models;
using Flamme.Testing.Shared.Services;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.ComponentTests.Scenarios.Activities.Looping.ForEach.Workflows;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Looping.ForEach;

public class ForEachWorkflowTests : AppComponentTest
{
    private AsyncWorkflowRunner _workflowRunner = null!;

    public ForEachWorkflowTests(App app) : base(app)
    {
    }

    protected override ValueTask OnInitializeAsync()
    {
        _workflowRunner = Scope.ServiceProvider.GetRequiredService<AsyncWorkflowRunner>();
        return ValueTask.CompletedTask;
    }

    [Test]
    [DisplayName("ForEach activity executes child activity for each collection item and supports blocking activities")]
    public async Task ForEachActivity_ExecutesChildActivity_ForEachCollectionItem_AndSupportsBlocking()
    {
        var result = await _workflowRunner.RunAndAwaitWorkflowCompletionAsync(WorkflowDefinitionHandle.ByDefinitionId(ForEachWorkflow.DefinitionId, VersionOptions.Published));
        var writeLineExecutionRecords = result.ActivityExecutionRecords.Where(x => x.ActivityId == "WriteLine1").ToList();
        
        // Assert that the workflow executed the expected number of activities.
        await Assert.That(writeLineExecutionRecords.Count).IsEqualTo(3);
    }
}
