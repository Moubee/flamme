using Elsa.Mediator.Contracts;
using Elsa.Workflows;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.Handlers;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Notifications;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Results;
using Elsa.Workflows.Runtime.Stimuli;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Handlers;

public class ResumeDispatchWorkflowActivityTests
{
    [Test]
    public async Task HandleAsync_ActivationDenied_RoutesParentBookmarkResumeWithDefinedOutcome()
    {
        var bookmarkQueue = IBookmarkQueue.Mock().Object;
        var stimulusHasher = IStimulusHasher.Mock().Object;
        var stimulusSender = IStimulusSender.Mock().Object;
        var handler = new ResumeDispatchWorkflowActivity(bookmarkQueue, stimulusHasher, stimulusSender);
        StimulusMetadata? observedMetadata = null;
        Mock.Get(stimulusSender).SendAsync(Arg.Any<string>(), Arg.Any<StimulusMetadata?>(), Arg.Any<CancellationToken>())
                    .Returns((string __arg0, global::Elsa.Workflows.Runtime.StimulusMetadata? __arg1, global::System.Threading.CancellationToken __arg2) =>
                    {
                        observedMetadata = __arg1;
                        return Task.FromResult(new SendStimulusResult([]));
                    });

        await handler.HandleAsync(new DispatchWorkflowActivationDenied("parent-1", "child-1", "Elsa.DispatchWorkflow", "dispatch-stimulus-hash"), CancellationToken.None);

        Mock.Get(stimulusSender).SendAsync(
            "dispatch-stimulus-hash", Arg.Any<StimulusMetadata?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(observedMetadata).IsNotNull();
        await Assert.That(observedMetadata.WorkflowInstanceId).IsEqualTo("parent-1");
        var input = (await Assert.That(observedMetadata.Input).IsTypeOf<Dictionary<string, object>>())!;
        await Assert.That((bool)input["CannotStart"]!).IsTrue();
        await Assert.That(input["WorkflowInstanceId"]).IsEqualTo("child-1");
        Mock.Get(stimulusHasher).Hash(Arg.Any<string>(), Arg.Is<object?>(__value => __value is DispatchWorkflowStimulus), null).WasNeverCalled();
        Mock.Get(bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task HandleAsync_ActivationDeniedBeforeParentBookmark_QueuesResumeForExactParentRoute()
    {
        var bookmarkQueue = IBookmarkQueue.Mock().Object;
        NewBookmarkQueueItem? enqueuedItem = null;
        Mock.Get(bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).Callback((NewBookmarkQueueItem item, CancellationToken _) => enqueuedItem = item);
        var workflowResumer = IWorkflowResumer.Mock().Object;
        Mock.Get(workflowResumer).ResumeAsync(Arg.Any<BookmarkFilter>(), Arg.Any<ResumeBookmarkOptions?>(), Arg.Any<CancellationToken>()).Returns([]);
        var stimulusSender = new StimulusSender(
            IStimulusHasher.Mock().Object,
            ITriggerBoundWorkflowService.Mock().Object,
            workflowResumer,
            bookmarkQueue,
            ITriggerInvoker.Mock().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StimulusSender>.Instance);
        var handler = new ResumeDispatchWorkflowActivity(bookmarkQueue, IStimulusHasher.Mock().Object, stimulusSender);

        await handler.HandleAsync(
            new DispatchWorkflowActivationDenied("parent-1", "child-1", "Elsa.DispatchWorkflow", "dispatch-stimulus-hash"),
            CancellationToken.None);

        await Assert.That(enqueuedItem).IsNotNull();
        await Assert.That(enqueuedItem.WorkflowInstanceId).IsEqualTo("parent-1");
        await Assert.That(enqueuedItem.StimulusHash).IsEqualTo("dispatch-stimulus-hash");
        var input = (await Assert.That(enqueuedItem.Options!.Input).IsTypeOf<Dictionary<string, object>>())!;
        await Assert.That((bool)input["CannotStart"]!).IsTrue();
        await Assert.That(input["WorkflowInstanceId"]).IsEqualTo("child-1");
        Mock.Get(workflowResumer).ResumeAsync(
            Arg.Is<BookmarkFilter>(filter => filter.WorkflowInstanceId == "parent-1" && filter.Hash == "dispatch-stimulus-hash"), Arg.Any<ResumeBookmarkOptions?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }
}
