using Flamme.Workflows;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using Flamme.Workflows.Runtime.Messages;
using Flamme.Workflows.Runtime.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Services;

public class StimulusSenderTests
{
    private const string ActivityTypeName = "Elsa.RunTask";
    private const string StimulusHash = "stimulus-hash";
    private const string ActivityInstanceId = "activity-instance-1";
    private const string WorkflowInstanceId = "workflow-instance-1";
    private const string BookmarkId = "bookmark-1";
    private const string CorrelationId = "correlation-1";

    private readonly IStimulusHasher _stimulusHasher = IStimulusHasher.Mock().Object;
    private readonly ITriggerBoundWorkflowService _triggerBoundWorkflowService = ITriggerBoundWorkflowService.Mock().Object;
    private readonly IWorkflowResumer _workflowResumer = IWorkflowResumer.Mock().Object;
    private readonly IBookmarkQueue _bookmarkQueue = IBookmarkQueue.Mock().Object;
    private readonly ITriggerInvoker _triggerInvoker = ITriggerInvoker.Mock().Object;

    public StimulusSenderTests()
    {
        Mock.Get(_stimulusHasher).Hash(ActivityTypeName, Arg.Any<object?>(), ActivityInstanceId).Returns(StimulusHash);
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<BookmarkFilter>(), Arg.Any<ResumeBookmarkOptions?>(), Arg.Any<CancellationToken>())
                    .Returns([]);
        Mock.Get(_triggerBoundWorkflowService).FindManyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns([]);
    }

    [Test]
    public async Task SendAsync_WhenUnmatchedResume_EnqueuesActivityInstanceIdAndActivityTypeName()
    {
        var sender = CreateSender();
        var metadata = CreateMetadata();
        NewBookmarkQueueItem? enqueued = null;
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).Callback((NewBookmarkQueueItem item, CancellationToken _) => enqueued = item);

        await sender.SendAsync(ActivityTypeName, new object(), metadata);

        await Assert.That(enqueued).IsNotNull();
        await Assert.That(enqueued.ActivityInstanceId).IsEqualTo(ActivityInstanceId);
        await Assert.That(enqueued.ActivityTypeName).IsEqualTo(ActivityTypeName);
        await Assert.That(enqueued.WorkflowInstanceId).IsEqualTo(WorkflowInstanceId);
        await Assert.That(enqueued.BookmarkId).IsEqualTo(BookmarkId);
        await Assert.That(enqueued.CorrelationId).IsEqualTo(CorrelationId);
        await Assert.That(enqueued.StimulusHash).IsEqualTo(StimulusHash);

        var filter = CreateBookmarkFilter(enqueued);
        await Assert.That(filter.ActivityInstanceId).IsEqualTo(ActivityInstanceId);
        await Assert.That(filter.Name).IsEqualTo(ActivityTypeName);
        await Assert.That(filter.Hash).IsEqualTo(StimulusHash);
    }

    [Test]
    public async Task SendAsync_WhenUnmatchedResumeWithHashOnly_EnqueuesActivityInstanceIdWithoutActivityTypeName()
    {
        var sender = CreateSender();
        var metadata = CreateMetadata();
        NewBookmarkQueueItem? enqueued = null;
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).Callback((NewBookmarkQueueItem item, CancellationToken _) => enqueued = item);

        await sender.SendAsync(StimulusHash, metadata);

        await Assert.That(enqueued).IsNotNull();
        await Assert.That(enqueued.ActivityInstanceId).IsEqualTo(ActivityInstanceId);
        await Assert.That(enqueued.ActivityTypeName).IsNull();

        var filter = CreateBookmarkFilter(enqueued);
        await Assert.That(filter.ActivityInstanceId).IsEqualTo(ActivityInstanceId);
        await Assert.That(filter.Name).IsNull();
    }

    [Test]
    public async Task SendAsync_WhenResumeMatches_DoesNotEnqueue()
    {
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<BookmarkFilter>(), Arg.Any<ResumeBookmarkOptions?>(), Arg.Any<CancellationToken>())
                    .Returns([new RunWorkflowInstanceResponse { WorkflowInstanceId = WorkflowInstanceId }]);
        var sender = CreateSender();

        await sender.SendAsync(ActivityTypeName, new object(), CreateMetadata());

        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    private StimulusSender CreateSender()
    {
        return new(
            _stimulusHasher,
            _triggerBoundWorkflowService,
            _workflowResumer,
            _bookmarkQueue,
            _triggerInvoker,
            NullLogger<StimulusSender>.Instance);
    }

    private static StimulusMetadata CreateMetadata()
    {
        return new()
        {
            WorkflowInstanceId = WorkflowInstanceId,
            ActivityInstanceId = ActivityInstanceId,
            BookmarkId = BookmarkId,
            CorrelationId = CorrelationId
        };
    }

    private static BookmarkFilter CreateBookmarkFilter(NewBookmarkQueueItem item)
    {
        return new BookmarkQueueItem
        {
            WorkflowInstanceId = item.WorkflowInstanceId,
            BookmarkId = item.BookmarkId,
            CorrelationId = item.CorrelationId,
            StimulusHash = item.StimulusHash,
            ActivityInstanceId = item.ActivityInstanceId,
            ActivityTypeName = item.ActivityTypeName,
            Options = item.Options
        }.CreateBookmarkFilter();
    }
}
