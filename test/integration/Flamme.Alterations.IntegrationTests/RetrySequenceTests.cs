using Flamme.Alterations.AlterationTypes;
using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Extensions;
using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Activities.Flowchart.Activities;
using Flamme.Workflows.Activities.Flowchart.Models;
using Flamme.Workflows.IncidentStrategies;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Activities;
using Flamme.Workflows.Runtime.Messages;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core.Interfaces;

namespace Flamme.Alterations.IntegrationTests;

public sealed class RetrySequenceTests : IAsyncInitializer, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly CapturingTextWriter _output = new();
    private readonly RetryProbe _probe = new();

    public RetrySequenceTests()
    {
        _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .WithCapturingTextWriter(_output)
            .ConfigureServices(services => services.AddSingleton(_probe))
            .ConfigureFlamme(flamme => flamme.UseAlterations())
            .AddWorkflow<RetrySequenceWorkflow>()
            .AddWorkflow<RetryStandaloneSequenceWorkflow>()
            .AddActivitiesFrom<RetryBookmarkActivity>()
            .Build();
    }

    public Task InitializeAsync() => _services.PopulateRegistriesAsync();
    public ValueTask DisposeAsync() => ((IAsyncDisposable)_services).DisposeAsync();

    [Test]
    [Arguments(nameof(RetrySequenceWorkflow))]
    [Arguments(nameof(RetryStandaloneSequenceWorkflow))]
    public async Task RetriedChildCompletesItsSequenceAndPreservesTheNextBookmark(string definitionId)
    {
        var runtime = _services.GetRequiredService<IWorkflowRuntime>();
        var client = await runtime.CreateClientAsync();
        await client.CreateInstanceAsync(new()
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published)
        });
        var firstRun = await client.RunInstanceAsync(RunWorkflowInstanceRequest.Empty);
        var faultedState = await client.ExportStateAsync();
        var incident = await Assert.That(faultedState.Incidents).HasSingleItem();
        await Assert.That(incident.ActivityId).IsEqualTo("Retry");
        var faultedContext = await Assert.That(faultedState.ActivityExecutionContexts).HasSingleItem(x => x.Status == ActivityStatus.Faulted);
        var sequenceContextId = faultedContext.ParentContextId;
        await Assert.That(_probe.Attempts).IsEqualTo(1);
        await Assert.That(_output.Lines).IsEmpty();

        var alterations = new IAlteration[] { new ScheduleActivity { ActivityInstanceId = faultedContext.Id } };
        var results = await _services.GetRequiredService<IAlterationRunner>().RunAsync([firstRun.WorkflowInstanceId], alterations);
        var alterationResult = await Assert.That(results).HasSingleItem();
        await Assert.That(alterationResult.IsSuccessful).IsTrue();

        var alteredState = await client.ExportStateAsync();
        var callbacks = alteredState.CompletionCallbacks
            .Where(x => x.ChildNodeId == faultedContext.ScheduledActivityNodeId)
            .ToList();
        await Assert.That(callbacks).IsNotEmpty();
        foreach (var callback in callbacks)
            await Assert.That(callback.OwnerInstanceId).IsEqualTo(sequenceContextId);

        await client.RunInstanceAsync(RunWorkflowInstanceRequest.Empty);
        var retriedState = await client.ExportStateAsync();
        var retryBookmark = await Assert.That(retriedState.Bookmarks).HasSingleItem();
        await Assert.That(retryBookmark.ActivityInstanceId).IsEqualTo(faultedContext.Id);
        await Assert.That(_probe.Attempts).IsEqualTo(2);

        var afterRetry = await client.RunInstanceAsync(new() { BookmarkId = retryBookmark.Id });
        var nextState = await client.ExportStateAsync();
        var nextBookmark = await Assert.That(nextState.Bookmarks).HasSingleItem();
        await Assert.That(afterRetry.Status).IsEqualTo(WorkflowStatus.Running);
        await Assert.That(_output.Lines).IsEquivalentTo(["Sequence finished"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(nextState.Incidents.Count).IsEqualTo(faultedState.Incidents.Count);
        await Assert.That(nextBookmark.ActivityInstanceId).IsNotEqualTo(retryBookmark.ActivityInstanceId);

        var finalRun = await client.RunInstanceAsync(new() { BookmarkId = nextBookmark.Id });
        await Assert.That(finalRun.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(_output.Lines).IsEquivalentTo(["Sequence finished", "Done"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(_probe.Attempts).IsEqualTo(2);
        await Assert.That((await client.ExportStateAsync()).Bookmarks).IsEmpty();
    }

    public sealed class RetryProbe
    {
        public int Attempts { get; set; }
    }

    public sealed class RetryBookmarkActivity : Activity
    {
        protected override void Execute(ActivityExecutionContext context)
        {
            if (++context.GetRequiredService<RetryProbe>().Attempts == 1)
            {
                throw new InvalidOperationException("Transient test failure");
            }

            context.CreateBookmark(new CreateBookmarkArgs());
        }
    }

    public class RetrySequenceWorkflow : WorkflowBase
    {
        protected virtual bool UseFlowchart => true;

        protected override void Build(IWorkflowBuilder builder)
        {
            builder.WorkflowOptions.IncidentStrategyType = typeof(ContinueWithIncidentsStrategy);
            var sequence = new Sequence
            {
                Activities = { new RetryBookmarkActivity { Id = "Retry" }, new WriteLine("Sequence finished") }
            };
            var next = new Event("Next");
            var end = new WriteLine("Done");
            builder.Root = UseFlowchart
                ? new Flowchart
                {
                    Activities = { sequence, next, end },
                    Connections = { new Connection(sequence, next), new Connection(next, end) }
                }
                : new Sequence { Activities = { sequence, next, end } };
        }
    }

    public sealed class RetryStandaloneSequenceWorkflow : RetrySequenceWorkflow
    {
        protected override bool UseFlowchart => false;
    }
}
