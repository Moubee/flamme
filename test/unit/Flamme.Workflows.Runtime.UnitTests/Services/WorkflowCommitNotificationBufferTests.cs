using Elsa.Mediator.Contracts;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class WorkflowCommitNotificationBufferTests
{
    [Test]
    public async Task SendAsync_WhenBuffering_DoesNotPublishUntilScopeIsFlushed()
    {
        var mediator = IMediator.Mock().Object;
        var buffer = CreateBuffer(mediator);
        var sender = new WorkflowCommitNotificationSender(mediator, buffer);
        var notification = new TestNotification();

        using var scope = buffer.Begin();
        await sender.SendAsync(notification);

        Mock.Get(mediator).SendAsync(notification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>()).WasNeverCalled();

        await scope.FlushAsync();

        Mock.Get(mediator).SendAsync(notification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task SendAsync_AfterScopeIsFlushed_PublishesImmediately()
    {
        var mediator = IMediator.Mock().Object;
        var buffer = CreateBuffer(mediator);
        var sender = new WorkflowCommitNotificationSender(mediator, buffer);
        var bufferedNotification = new TestNotification();
        var subsequentNotification = new TestNotification();
        var notificationPublishingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueNotificationPublishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock.Get(mediator).SendAsync(bufferedNotification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>())
                    .Returns(async () =>
                    {
                        notificationPublishingStarted.SetResult();
                        await continueNotificationPublishing.Task;
                    });

        using var scope = buffer.Begin();
        await sender.SendAsync(bufferedNotification);
        var flushTask = scope.FlushAsync();
        await notificationPublishingStarted.Task;
        continueNotificationPublishing.SetResult();
        await flushTask;
        await sender.SendAsync(subsequentNotification);

        Mock.Get(mediator).SendAsync(bufferedNotification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(mediator).SendAsync(subsequentNotification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task SendAsync_WhenBufferingScopeIsDisposedWithoutFlush_DiscardsNotifications()
    {
        var mediator = IMediator.Mock().Object;
        var buffer = CreateBuffer(mediator);
        var sender = new WorkflowCommitNotificationSender(mediator, buffer);
        var notification = new TestNotification();

        using (buffer.Begin())
        {
            await sender.SendAsync(notification);
        }

        Mock.Get(mediator).SendAsync(notification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task FlushAsync_WhenNotificationFails_StillPublishesRemainingNotifications()
    {
        var mediator = IMediator.Mock().Object;
        var buffer = CreateBuffer(mediator);
        var sender = new WorkflowCommitNotificationSender(mediator, buffer);
        var failingNotification = new TestNotification();
        var succeedingNotification = new TestNotification();
        Mock.Get(mediator).SendAsync(failingNotification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>())
                    .Returns(() => throw new InvalidOperationException("Handler failed"));

        using var scope = buffer.Begin();
        await sender.SendAsync(failingNotification);
        await sender.SendAsync(succeedingNotification);

        await Assert.ThrowsExactlyAsync<AggregateException>(() => scope.FlushAsync());

        Mock.Get(mediator).SendAsync(failingNotification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(mediator).SendAsync(succeedingNotification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FlushAsync_WhenNotificationIsCanceled_PropagatesCancellation()
    {
        var mediator = IMediator.Mock().Object;
        var buffer = CreateBuffer(mediator);
        var sender = new WorkflowCommitNotificationSender(mediator, buffer);
        var notification = new TestNotification();
        Mock.Get(mediator).SendAsync(notification, Arg.Any<global::Elsa.Mediator.Contracts.IEventPublishingStrategy?>(), Arg.Any<CancellationToken>())
                    .Returns(() => throw new OperationCanceledException());

        using var scope = buffer.Begin();
        await sender.SendAsync(notification);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => scope.FlushAsync());
    }

    private static WorkflowCommitNotificationBuffer CreateBuffer(IMediator mediator) => new(mediator, ILogger<WorkflowCommitNotificationBuffer>.Mock().Object);

    private class TestNotification : INotification;
}
