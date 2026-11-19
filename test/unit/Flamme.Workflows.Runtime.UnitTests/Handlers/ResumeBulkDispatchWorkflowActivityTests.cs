using Flamme.Workflows;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Runtime.Activities;
using Flamme.Workflows.Runtime.Handlers;
using Flamme.Workflows.Runtime.Notifications;
using Flamme.Workflows.Runtime.Results;
using Flamme.Workflows.Runtime.Stimuli;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Handlers;

public class ResumeBulkDispatchWorkflowActivityTests
{
    [Test]
    public async Task HandleAsync_ActivationDenied_RoutesFaultedChildCompletionToParentBookmark()
    {
        var bookmarkQueue = IBookmarkQueue.Mock().Object;
        var stimulusHasher = IStimulusHasher.Mock().Object;
        var stimulusSender = IStimulusSender.Mock().Object;
        StimulusMetadata? observedMetadata = null;
        Mock.Get(stimulusSender).SendAsync(Arg.Any<string>(), Arg.Any<StimulusMetadata?>(), Arg.Any<CancellationToken>())
                    .Returns((string __arg0, global::Flamme.Workflows.Runtime.StimulusMetadata? __arg1, global::System.Threading.CancellationToken __arg2) =>
                    {
                        observedMetadata = __arg1;
                        return Task.FromResult(new SendStimulusResult([]));
                    });
        var handler = new ResumeBulkDispatchWorkflowActivity(bookmarkQueue, stimulusHasher, stimulusSender);
        var activityTypeName = ActivityTypeNameHelper.GenerateTypeName<BulkDispatchWorkflows>();

        await handler.HandleAsync(
            new DispatchWorkflowActivationDenied("parent-1", "child-1", activityTypeName, "bulk-stimulus-hash"),
            CancellationToken.None);

        Mock.Get(stimulusSender).SendAsync(
            "bulk-stimulus-hash", Arg.Any<StimulusMetadata?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(observedMetadata).IsNotNull();
        await Assert.That(observedMetadata.WorkflowInstanceId).IsEqualTo("parent-1");
        var input = (await Assert.That(observedMetadata.Input).IsTypeOf<Dictionary<string, object>>())!;
        await Assert.That((bool)input["CannotStart"]!).IsTrue();
        await Assert.That(input["WorkflowInstanceId"]).IsEqualTo("child-1");
        await Assert.That(input["WorkflowStatus"]).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(input["WorkflowSubStatus"]).IsEqualTo(WorkflowSubStatus.Faulted);
        await Assert.That(input["WorkflowOutput"]).IsTypeOf<Dictionary<string, object>>();
        Mock.Get(bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task HandleAsync_ActivationDeniedForAnotherActivity_DoesNotRouteToParent()
    {
        var stimulusSender = IStimulusSender.Mock().Object;
        var handler = new ResumeBulkDispatchWorkflowActivity(
            IBookmarkQueue.Mock().Object,
            IStimulusHasher.Mock().Object,
            stimulusSender);

        await handler.HandleAsync(
            new DispatchWorkflowActivationDenied("parent-1", "child-1", "Elsa.DispatchWorkflow", "single-stimulus-hash"),
            CancellationToken.None);

        Mock.Get(stimulusSender).SendAsync(
            Arg.Any<string>(), Arg.Any<StimulusMetadata?>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }
}
