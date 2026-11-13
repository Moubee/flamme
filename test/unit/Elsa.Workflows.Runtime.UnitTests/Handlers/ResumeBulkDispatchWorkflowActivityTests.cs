using Elsa.Workflows;
using Elsa.Workflows.Helpers;
using Elsa.Workflows.Runtime.Activities;
using Elsa.Workflows.Runtime.Handlers;
using Elsa.Workflows.Runtime.Notifications;
using Elsa.Workflows.Runtime.Results;
using Elsa.Workflows.Runtime.Stimuli;
using NSubstitute;

namespace Elsa.Workflows.Runtime.UnitTests.Handlers;

public class ResumeBulkDispatchWorkflowActivityTests
{
    [Test]
    public async Task HandleAsync_ActivationDenied_RoutesFaultedChildCompletionToParentBookmark()
    {
        var bookmarkQueue = Substitute.For<IBookmarkQueue>();
        var stimulusHasher = Substitute.For<IStimulusHasher>();
        var stimulusSender = Substitute.For<IStimulusSender>();
        StimulusMetadata? observedMetadata = null;
        stimulusSender.SendAsync(Arg.Any<string>(), Arg.Any<StimulusMetadata>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observedMetadata = call.Arg<StimulusMetadata>();
                return Task.FromResult(new SendStimulusResult([]));
            });
        var handler = new ResumeBulkDispatchWorkflowActivity(bookmarkQueue, stimulusHasher, stimulusSender);
        var activityTypeName = ActivityTypeNameHelper.GenerateTypeName<BulkDispatchWorkflows>();

        await handler.HandleAsync(
            new DispatchWorkflowActivationDenied("parent-1", "child-1", activityTypeName, "bulk-stimulus-hash"),
            CancellationToken.None);

        await stimulusSender.Received(1).SendAsync(
            "bulk-stimulus-hash",
            Arg.Any<StimulusMetadata>(),
            Arg.Any<CancellationToken>());
        await Assert.That(observedMetadata).IsNotNull();
        await Assert.That(observedMetadata.WorkflowInstanceId).IsEqualTo("parent-1");
        var input = (await Assert.That(observedMetadata.Input).IsTypeOf<Dictionary<string, object>>())!;
        await Assert.That((bool)input["CannotStart"]!).IsTrue();
        await Assert.That(input["WorkflowInstanceId"]).IsEqualTo("child-1");
        await Assert.That(input["WorkflowStatus"]).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(input["WorkflowSubStatus"]).IsEqualTo(WorkflowSubStatus.Faulted);
        await Assert.That(input["WorkflowOutput"]).IsTypeOf<Dictionary<string, object>>();
        await bookmarkQueue.DidNotReceive().EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleAsync_ActivationDeniedForAnotherActivity_DoesNotRouteToParent()
    {
        var stimulusSender = Substitute.For<IStimulusSender>();
        var handler = new ResumeBulkDispatchWorkflowActivity(
            Substitute.For<IBookmarkQueue>(),
            Substitute.For<IStimulusHasher>(),
            stimulusSender);

        await handler.HandleAsync(
            new DispatchWorkflowActivationDenied("parent-1", "child-1", "Elsa.DispatchWorkflow", "single-stimulus-hash"),
            CancellationToken.None);

        await stimulusSender.DidNotReceive().SendAsync(
            Arg.Any<string>(),
            Arg.Any<StimulusMetadata>(),
            Arg.Any<CancellationToken>());
    }
}
