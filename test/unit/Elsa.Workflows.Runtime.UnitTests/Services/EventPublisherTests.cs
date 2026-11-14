using Elsa.Workflows.Helpers;
using Elsa.Workflows.Runtime.Activities;
using Elsa.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Requests;
using Elsa.Workflows.Runtime.Stimuli;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class EventPublisherTests
{
    private readonly IStimulusSender _stimulusSender = Mock.Of<IStimulusSender>().Object;
    private readonly IWorkflowDispatcher _workflowDispatcher = Mock.Of<IWorkflowDispatcher>().Object;

    [Test]
    public async Task PublishAsync_WhenAsynchronous_DispatchesThroughWorkflowDispatcher()
    {
        var publisher = CreatePublisher();
        const string eventName = "OrderShipped";
        const string correlationId = "corr-1";
        const string workflowInstanceId = "wf-1";
        const string activityInstanceId = "act-1";
        var payload = new { Status = "Shipped" };

        await publisher.PublishAsync(eventName, correlationId, workflowInstanceId, activityInstanceId, payload, asynchronous: true);

        Mock.Get(_workflowDispatcher).DispatchAsync(
            Arg.Is<DispatchTriggerWorkflowsRequest>(request => IsMatchingTriggerRequest(request, eventName, correlationId, workflowInstanceId, activityInstanceId, payload)), Arg.Any<global::Elsa.Workflows.Runtime.DispatchWorkflowOptions?>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_stimulusSender).SendAsync(Any(), Any(), Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task PublishAsync_WhenSynchronous_SendsThroughStimulusSender()
    {
        var publisher = CreatePublisher();
        const string eventName = "OrderShipped";
        var payload = new Dictionary<string, object> { ["Status"] = "Shipped" };

        await publisher.PublishAsync(eventName, payload: payload, asynchronous: false);

        Mock.Get(_stimulusSender).SendAsync(
            ActivityTypeNameHelper.GenerateTypeName<Event>(), Arg.Is<object>(__value => __value is EventStimulus __typed && (__typed.EventName == eventName)), Arg.Is<StimulusMetadata?>(metadata =>
                metadata != null && metadata.Input != null &&
                metadata.Input[Event.EventInputWorkflowInputKey] == payload), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_workflowDispatcher).DispatchAsync(Arg.Any<DispatchTriggerWorkflowsRequest>(), Any(), Any()).WasNeverCalled();
    }

    private EventPublisher CreatePublisher() => new(_stimulusSender, _workflowDispatcher);

    private static bool IsMatchingTriggerRequest(
        DispatchTriggerWorkflowsRequest request,
        string eventName,
        string correlationId,
        string workflowInstanceId,
        string activityInstanceId,
        object payload)
    {
        var stimulus = request.BookmarkPayload as EventStimulus;
        return request.ActivityTypeName == ActivityTypeNameHelper.GenerateTypeName<Event>() &&
               stimulus != null &&
               stimulus.EventName == eventName &&
               request.CorrelationId == correlationId &&
               request.WorkflowInstanceId == workflowInstanceId &&
               request.ActivityInstanceId == activityInstanceId &&
               request.Input != null &&
               request.Input[Event.EventInputWorkflowInputKey] == payload;
    }
}
