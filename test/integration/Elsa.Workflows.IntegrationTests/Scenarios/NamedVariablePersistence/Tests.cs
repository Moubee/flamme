using Elsa.Extensions;
using Elsa.Testing.Shared;
using Elsa.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.IntegrationTests.Scenarios.NamedVariablePersistence;

public class Tests : IAsyncDisposable
{
    private readonly IWorkflowRunner _workflowRunner;
    private readonly CapturingTextWriter _capturingTextWriter = new();
    private readonly IWorkflowBuilderFactory _workflowBuilderFactory;
    private readonly IServiceProvider _services;

    public Tests()
    {
        _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput).WithCapturingTextWriter(_capturingTextWriter).Build();
        _workflowBuilderFactory = _services.GetRequiredService<IWorkflowBuilderFactory>();
        _workflowRunner = _services.GetRequiredService<IWorkflowRunner>();
    }

    public ValueTask DisposeAsync() => TestResourceDisposal.DisposeAsync(_services);

    [Test]
    [DisplayName("Named WithVariable value survives suspend and resume")]
    public async Task NamedWithVariable_ValueSurvivesSuspendAndResume()
    {
        // Arrange
        await _services.PopulateRegistriesAsync();
        var workflow = await _workflowBuilderFactory.CreateBuilder().BuildWorkflowAsync<NamedVariableSurvivesSuspendWorkflow>();

        // Act
        var started = await _workflowRunner.RunAsync(workflow);
        var bookmark = started.WorkflowState.Bookmarks.Single(x => x.ActivityId == "Resume");
        var runOptions = new RunWorkflowOptions { BookmarkId = bookmark.Id };
        var resumed = await _workflowRunner.RunAsync(workflow, started.WorkflowState, runOptions);

        // Assert
        await Assert.That(started.WorkflowState.Status).IsEqualTo(WorkflowStatus.Running);
        await Assert.That(started.WorkflowState.SubStatus).IsEqualTo(WorkflowSubStatus.Suspended);
        await Assert.That(resumed.WorkflowState.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(_capturingTextWriter.Lines.ToList()).IsEquivalentTo([
                "before suspend: hello",
                "after resume: hello"
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
