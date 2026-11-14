using Elsa.Mediator.Contracts;
using Elsa.Testing.Shared;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Notifications;
using Elsa.Workflows.Runtime.Requests;
using Elsa.Workflows.State;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class DefaultCommitStateHandlerTests
{
    [Test]
    public async Task CommitAsync_ExecutesPersistenceInsideCommitTransaction()
    {
        await using var fixture = await CommitTestFixture.CreateAsync();
        Mock.Get(fixture.WorkflowInstanceManager).SaveAsync(fixture.WorkflowState, Arg.Any<CancellationToken>()).Returns(fixture.WorkflowInstance);
        var bookmarksPersistedInsideTransaction = false;
        var activityLogsPersistedInsideTransaction = false;
        var workflowLogsPersistedInsideTransaction = false;
        var variablesSavedInsideTransaction = false;
        var workflowSavedInsideTransaction = false;
        Mock.Get(fixture.BookmarkPersister).PersistBookmarksAsync(Arg.Any<UpdateBookmarksRequest>()).Callback(() => bookmarksPersistedInsideTransaction = fixture.Transaction.IsExecuting);
        Mock.Get(fixture.ActivityExecutionLogSink).PersistExecutionLogsAsync(fixture.WorkflowExecutionContext, Arg.Any<CancellationToken>()).Callback(() => activityLogsPersistedInsideTransaction = fixture.Transaction.IsExecuting);
        Mock.Get(fixture.WorkflowExecutionLogSink).PersistExecutionLogsAsync(fixture.WorkflowExecutionContext, Arg.Any<CancellationToken>()).Callback(() => workflowLogsPersistedInsideTransaction = fixture.Transaction.IsExecuting);
        Mock.Get(fixture.VariablePersistenceManager).SaveVariablesAsync(fixture.WorkflowExecutionContext).Callback(() => variablesSavedInsideTransaction = fixture.Transaction.IsExecuting);
        Mock.Get(fixture.WorkflowInstanceManager).SaveAsync(fixture.WorkflowState, Arg.Any<CancellationToken>()).Callback(() => workflowSavedInsideTransaction = fixture.Transaction.IsExecuting).Returns(fixture.WorkflowInstance);

        await fixture.Handler.CommitAsync(fixture.WorkflowExecutionContext, fixture.WorkflowState);

        await Assert.That(bookmarksPersistedInsideTransaction).IsTrue();
        await Assert.That(activityLogsPersistedInsideTransaction).IsTrue();
        await Assert.That(workflowLogsPersistedInsideTransaction).IsTrue();
        await Assert.That(variablesSavedInsideTransaction).IsTrue();
        await Assert.That(workflowSavedInsideTransaction).IsTrue();
        await Assert.That(fixture.Transaction.Completed).IsTrue();
        await Assert.That(fixture.ActivityExecutionContext.IsDirty).IsFalse();
        Mock.Get(fixture.NotificationSender).SendAsync(
            Arg.Is<global::Elsa.Mediator.Contracts.INotification>(__value => __value is WorkflowStateCommitted __typed && (__typed.WorkflowInstance == fixture.WorkflowInstance && __typed.WorkflowState == fixture.WorkflowState)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task CommitAsync_WhenPersistenceFails_DoesNotClearExecutionLogOrPublishCommittedNotification()
    {
        await using var fixture = await CommitTestFixture.CreateAsync();
        fixture.WorkflowExecutionContext.AddExecutionLogEntry("Started");
        Mock.Get(fixture.WorkflowInstanceManager).SaveAsync(fixture.WorkflowState, Arg.Any<CancellationToken>()).Returns(() => throw new InvalidOperationException("state save failed"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Handler.CommitAsync(fixture.WorkflowExecutionContext, fixture.WorkflowState));

        await Assert.That(fixture.Transaction.Executed).IsTrue();
        await Assert.That(fixture.Transaction.Completed).IsFalse();
        await Assert.That(fixture.ActivityExecutionContext.IsDirty).IsTrue();
        await Assert.That(fixture.WorkflowExecutionContext.ExecutionLog).IsNotEmpty();
        Mock.Get(fixture.NotificationSender).SendAsync(Arg.Is<global::Elsa.Mediator.Contracts.INotification>(__value => __value is WorkflowStateCommitted), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task CommitAsync_WhenBufferedNotificationFlushFails_PublishesCommittedNotification()
    {
        await using var fixture = await CommitTestFixture.CreateAsync();
        var bufferedNotificationSender = new WorkflowCommitNotificationSender(fixture.Mediator, fixture.NotificationBuffer);
        var bufferedNotification = new TestNotification();
        Mock.Get(fixture.WorkflowInstanceManager).SaveAsync(fixture.WorkflowState, Arg.Any<CancellationToken>()).Returns(fixture.WorkflowInstance);
        Mock.Get(fixture.ActivityExecutionLogSink).PersistExecutionLogsAsync(fixture.WorkflowExecutionContext, Arg.Any<CancellationToken>())
                    .Returns(() => bufferedNotificationSender.SendAsync(bufferedNotification));
        Mock.Get(fixture.Mediator).SendAsync(bufferedNotification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>())
                    .Returns(() => throw new InvalidOperationException("Buffered handler failed"));

        await Assert.ThrowsExactlyAsync<AggregateException>(() => fixture.Handler.CommitAsync(fixture.WorkflowExecutionContext, fixture.WorkflowState));

        Mock.Get(fixture.NotificationSender).SendAsync(
            Arg.Is<global::Elsa.Mediator.Contracts.INotification>(__value => __value is WorkflowStateCommitted __typed && (__typed.WorkflowInstance == fixture.WorkflowInstance && __typed.WorkflowState == fixture.WorkflowState)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    private class CommitTestFixture : IAsyncDisposable
    {
        private CommitTestFixture(ActivityExecutionContext activityExecutionContext)
        {
            ActivityExecutionContext = activityExecutionContext;
            ActivityExecutionContext.Taint();
            WorkflowExecutionContext = activityExecutionContext.WorkflowExecutionContext;
            WorkflowExecutionContext.AddActivityExecutionContext(activityExecutionContext);
            WorkflowState = new() { Id = WorkflowExecutionContext.Id };
            WorkflowInstance = new() { Id = WorkflowExecutionContext.Id };
            NotificationBuffer = CreateBuffer(Mediator);
            Handler = new(
                WorkflowInstanceManager,
                BookmarkPersister,
                VariablePersistenceManager,
                Transaction,
                NotificationBuffer,
                NotificationSender,
                ActivityExecutionLogSink,
                WorkflowExecutionLogSink);
        }

        public WorkflowExecutionContext WorkflowExecutionContext { get; }
        public ActivityExecutionContext ActivityExecutionContext { get; }
        public WorkflowState WorkflowState { get; }
        public WorkflowInstance WorkflowInstance { get; }
        public IWorkflowInstanceManager WorkflowInstanceManager { get; } = Mock.Of<IWorkflowInstanceManager>().Object;
        public IBookmarksPersister BookmarkPersister { get; } = Mock.Of<IBookmarksPersister>().Object;
        public IVariablePersistenceManager VariablePersistenceManager { get; } = Mock.Of<IVariablePersistenceManager>().Object;
        public IMediator Mediator { get; } = Mock.Of<IMediator>().Object;
        public WorkflowCommitNotificationBuffer NotificationBuffer { get; }
        public INotificationSender NotificationSender { get; } = Mock.Of<INotificationSender>().Object;
        public ILogRecordSink<ActivityExecutionRecord> ActivityExecutionLogSink { get; } = Mock.Of<ILogRecordSink<ActivityExecutionRecord>>().Object;
        public ILogRecordSink<WorkflowExecutionLogRecord> WorkflowExecutionLogSink { get; } = Mock.Of<ILogRecordSink<WorkflowExecutionLogRecord>>().Object;
        public RecordingWorkflowCommitTransaction Transaction { get; } = new();
        public DefaultCommitStateHandler Handler { get; }

        public static async Task<CommitTestFixture> CreateAsync()
        {
            var fixture = new ActivityTestFixture(new WriteLine("Test"));
            var activityExecutionContext = await fixture.BuildAsync();
            return new(activityExecutionContext);
        }

        public ValueTask DisposeAsync()
        {
            ((IDisposable)ActivityExecutionContext).Dispose();
            return ((IAsyncDisposable)WorkflowExecutionContext.ServiceProvider).DisposeAsync();
        }
    }

    private static WorkflowCommitNotificationBuffer CreateBuffer(IMediator mediator) => new(mediator, Mock.Of<ILogger<WorkflowCommitNotificationBuffer>>().Object);

    private class TestNotification : INotification;

    private class RecordingWorkflowCommitTransaction : IWorkflowCommitTransaction
    {
        public bool IsExecuting { get; private set; }
        public bool Executed { get; private set; }
        public bool Completed { get; private set; }

        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            Executed = true;
            IsExecuting = true;
            try
            {
                await operation(cancellationToken);
                Completed = true;
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }
}
