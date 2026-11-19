using Elsa.Testing.Shared;
using Elsa.Workflows.Activities;
using Elsa.Workflows.CommitStates;
using Elsa.Workflows.CommitStates.Strategies;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Core.UnitTests.Contexts;

public class ExecutionContextTimeTests
{
    private static readonly DateTimeOffset InitialUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    [Test]
    public async Task Contexts_UseTheConfiguredProviderForCreationLogsBookmarksAndCompletion()
    {
        var timeProvider = new FakeTimeProvider(InitialUtc);
        await using var fixture = new ActivityTestFixture(new WriteLine("test"))
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(timeProvider));
        var activityContext = await fixture.BuildAsync();
        var workflowContext = activityContext.WorkflowExecutionContext;

        await Assert.That(workflowContext.TimeProvider).IsSameReferenceAs(timeProvider);
        await Assert.That(workflowContext.CreatedAt).IsEqualTo(InitialUtc);
        await Assert.That(workflowContext.UpdatedAt).IsEqualTo(InitialUtc);
        await Assert.That(activityContext.StartedAt).IsEqualTo(InitialUtc);

        timeProvider.Advance(TimeSpan.FromTicks(37));
        var recordedAt = InitialUtc.AddTicks(37);
        var workflowLog = workflowContext.AddExecutionLogEntry("test workflow");
        var activityLog = activityContext.AddExecutionLogEntry("test activity");
        var bookmark = activityContext.CreateBookmark(new CreateBookmarkArgs { BookmarkId = "test-bookmark" });

        await Assert.That(workflowLog.Timestamp).IsEqualTo(recordedAt);
        await Assert.That(activityLog.Timestamp).IsEqualTo(recordedAt);
        await Assert.That(bookmark.CreatedAt).IsEqualTo(recordedAt);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        activityContext.TransitionTo(ActivityStatus.Running);
        await activityContext.CompleteActivityAsync();
        await Assert.That(activityContext.CompletedAt).IsEqualTo(recordedAt.AddSeconds(5));

        timeProvider.Advance(TimeSpan.FromSeconds(3));
        workflowContext.Cancel();
        await Assert.That(workflowContext.UpdatedAt).IsEqualTo(recordedAt.AddSeconds(8));
        await Assert.That(workflowContext.FinishedAt).IsEqualTo(recordedAt.AddSeconds(8));
    }

    [Test]
    public async Task RestoringState_PreservesStoredInstantsAfterProviderAdvances()
    {
        var timeProvider = new FakeTimeProvider(InitialUtc);
        await using var fixture = new ActivityTestFixture(new WriteLine("test"))
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(timeProvider));
        var activityContext = await fixture.BuildAsync();
        var workflowContext = activityContext.WorkflowExecutionContext;
        activityContext.CreateBookmark(new CreateBookmarkArgs { BookmarkId = "persisted-bookmark" });
        var extractor = workflowContext.GetRequiredService<IWorkflowStateExtractor>();
        var state = extractor.Extract(workflowContext);

        timeProvider.Advance(TimeSpan.FromDays(1));
        var restored = await WorkflowExecutionContext.CreateAsync(workflowContext.ServiceProvider, workflowContext.WorkflowGraph, state);

        await Assert.That(restored.TimeProvider).IsSameReferenceAs(timeProvider);
        await Assert.That(restored.CreatedAt).IsEqualTo(InitialUtc);
        await Assert.That(restored.UpdatedAt).IsEqualTo(InitialUtc);
        await Assert.That(restored.Bookmarks.Single().CreatedAt).IsEqualTo(InitialUtc);
        await Assert.That(restored.AddExecutionLogEntry("resumed").Timestamp).IsEqualTo(InitialUtc.AddDays(1));
    }

    [Test]
    public async Task PeriodicCommit_PreservesTheStrictIntervalBoundary()
    {
        var timeProvider = new FakeTimeProvider(InitialUtc);
        await using var fixture = new ActivityTestFixture(new WriteLine("test"))
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(timeProvider));
        var activityContext = await fixture.BuildAsync();
        var strategy = new PeriodicWorkflowStrategy(TimeSpan.FromSeconds(1));
        var strategyContext = new WorkflowCommitStateStrategyContext(activityContext.WorkflowExecutionContext, WorkflowLifetimeEvent.ActivityExecuted);

        await Assert.That(strategy.ShouldCommit(strategyContext)).IsEqualTo(CommitAction.Commit);
        timeProvider.Advance(TimeSpan.FromSeconds(1).Subtract(TimeSpan.FromTicks(1)));
        await Assert.That(strategy.ShouldCommit(strategyContext)).IsEqualTo(CommitAction.Default);
        timeProvider.Advance(TimeSpan.FromTicks(1));
        await Assert.That(strategy.ShouldCommit(strategyContext)).IsEqualTo(CommitAction.Default);
        timeProvider.Advance(TimeSpan.FromTicks(1));
        await Assert.That(strategy.ShouldCommit(strategyContext)).IsEqualTo(CommitAction.Commit);
    }
}
