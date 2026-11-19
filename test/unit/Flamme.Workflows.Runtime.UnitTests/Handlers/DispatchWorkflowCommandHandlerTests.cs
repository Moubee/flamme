using Flamme.Mediator.Contracts;
using Flamme.Mediator.Models;
using Flamme.Workflows.Runtime.Commands;
using Flamme.Workflows.Runtime.Handlers;
using Flamme.Workflows.Runtime.Messages;
using Flamme.Workflows.Runtime.Notifications;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Handlers;

public class DispatchWorkflowCommandHandlerTests
{
    private readonly IStimulusSender _stimulusSender = IStimulusSender.Mock().Object;
    private readonly IWorkflowRuntime _workflowRuntime = IWorkflowRuntime.Mock().Object;
    private readonly IWorkflowClient _workflowClient = IWorkflowClient.Mock().Object;
    private readonly INotificationSender _notificationSender = INotificationSender.Mock().Object;
    private readonly DispatchWorkflowCommandHandler _handler;

    public DispatchWorkflowCommandHandlerTests()
    {
        _handler = new(_stimulusSender, _workflowRuntime, _notificationSender);
        Mock.Get(_workflowRuntime).CreateClientAsync("child-1", Arg.Any<CancellationToken>()).Returns(_workflowClient);
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(Arg.Any<CreateAndRunWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                    .Returns(new RunWorkflowInstanceResponse());
    }

    [Test]
    public async Task HandleAsync_DoesNotCheckExistingInstanceAndCreatesAndRunsWorkflow_WhenIdempotencyIsNotRequested()
    {
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = "child-1",
            SkipIfInstanceExists = false
        };
        Mock.Get(_workflowClient).InstanceExistsAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _handler.HandleAsync(command, CancellationToken.None);

        Mock.Get(_workflowClient).InstanceExistsAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(
            Arg.Is<CreateAndRunWorkflowInstanceRequest>(x => x.WorkflowDefinitionHandle.DefinitionVersionId == "definition-version-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task HandleAsync_PreservesDefinitionDispatchMetadata()
    {
        var input = new Dictionary<string, object> { ["input-key"] = "input-value" };
        var properties = new Dictionary<string, object> { ["property-key"] = "property-value" };
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = "child-1",
            ParentWorkflowInstanceId = "parent-1",
            CorrelationId = "correlation-1",
            Input = input,
            Properties = properties,
            TriggerActivityId = "trigger-1",
            SchedulingActivityExecutionId = "scheduled-execution-1",
            SchedulingWorkflowInstanceId = "scheduling-parent-1",
            SchedulingCallStackDepth = 4
        };
        CreateAndRunWorkflowInstanceRequest? observedRequest = null;
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(
                        Arg.Any<CreateAndRunWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                    .Callback((CreateAndRunWorkflowInstanceRequest request, CancellationToken _) => observedRequest = request)
                    .Returns(new RunWorkflowInstanceResponse());

        await _handler.HandleAsync(command, CancellationToken.None);

        await Assert.That(observedRequest).IsNotNull();
        await Assert.That(observedRequest.WorkflowDefinitionHandle.DefinitionVersionId).IsEqualTo("definition-version-1");
        await Assert.That(observedRequest.CorrelationId).IsEqualTo("correlation-1");
        await Assert.That(observedRequest.Input).IsSameReferenceAs(input);
        await Assert.That(observedRequest.Properties).IsSameReferenceAs(properties);
        await Assert.That(observedRequest.ParentId).IsEqualTo("parent-1");
        await Assert.That(observedRequest.TriggerActivityId).IsEqualTo("trigger-1");
        await Assert.That(observedRequest.SchedulingActivityExecutionId).IsEqualTo("scheduled-execution-1");
        await Assert.That(observedRequest.SchedulingWorkflowInstanceId).IsEqualTo("scheduling-parent-1");
        await Assert.That(observedRequest.SchedulingCallStackDepth).IsEqualTo(4);
    }

    [Test]
    public async Task HandleAsync_DoesNotCreateAndRunWorkflow_WhenCommandInstanceAlreadyExistsAndIdempotencyIsRequested()
    {
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = "child-1",
            SkipIfInstanceExists = true
        };
        Mock.Get(_workflowClient).InstanceExistsAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _handler.HandleAsync(command, CancellationToken.None);

        Mock.Get(_workflowClient).InstanceExistsAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task HandleAsync_CreatesAndRunsWorkflow_WhenIdempotencyIsRequestedAndCommandInstanceDoesNotExist()
    {
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = "child-1",
            SkipIfInstanceExists = true
        };
        Mock.Get(_workflowClient).InstanceExistsAsync(Arg.Any<CancellationToken>()).Returns(false);

        await _handler.HandleAsync(command, CancellationToken.None);

        Mock.Get(_workflowClient).InstanceExistsAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(
            Arg.Is<CreateAndRunWorkflowInstanceRequest>(x => x.WorkflowDefinitionHandle.DefinitionVersionId == "definition-version-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task HandleAsync_ReportsActivationDenialForWaitingParent_WithoutChangingQueuedDispatchResult()
    {
        var properties = new Dictionary<string, object> { ["WaitForCompletion"] = true };
        DispatchWorkflowActivationDeniedRoute.Add(properties, "Workflows.Dispatch", "dispatch-hash");
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = "child-1",
            ParentWorkflowInstanceId = "parent-1",
            Properties = properties
        };
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(Arg.Any<CreateAndRunWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                    .Returns(new RunWorkflowInstanceResponse { CannotStart = true });

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        await Assert.That(result).IsSameReferenceAs(Unit.Instance);
        Mock.Get(_notificationSender).SendAsync(
            Arg.Is<INotification>(value => value is DispatchWorkflowActivationDenied notification && notification.ParentWorkflowInstanceId == "parent-1" && notification.WorkflowInstanceId == "child-1" && notification.ActivityTypeName == "Workflows.Dispatch" && notification.StimulusHash == "dispatch-hash"),
            Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));

        await Assert.That(properties["__elsa.internal.dispatch-activation-denied.activity-type-name"]).IsEqualTo("Workflows.Dispatch");
        await Assert.That(properties["__elsa.internal.dispatch-activation-denied.stimulus-hash"]).IsEqualTo("dispatch-hash");
    }

    [Test]
    public async Task HandleAsync_DoesNotReportActivationDenialForFireAndForgetDispatch()
    {
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = "child-1",
            ParentWorkflowInstanceId = "parent-1",
            Properties = new Dictionary<string, object> { ["WaitForCompletion"] = false }
        };
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(Arg.Any<CreateAndRunWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                    .Returns(new RunWorkflowInstanceResponse { CannotStart = true });

        await _handler.HandleAsync(command, CancellationToken.None);

        Mock.Get(_notificationSender).SendAsync(Arg.Is<INotification>(value => value is DispatchWorkflowActivationDenied), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task HandleAsync_SanitizesActivationDenialRouteBeforePersistingProperties()
    {
        var properties = new Dictionary<string, object> { ["WaitForCompletion"] = true };
        DispatchWorkflowActivationDeniedRoute.Add(properties, "Workflows.Dispatch", "dispatch-hash");
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = "child-1",
            ParentWorkflowInstanceId = "parent-1",
            Properties = properties
        };
        CreateAndRunWorkflowInstanceRequest? observedRequest = null;
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(
                        Arg.Any<CreateAndRunWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                    .Callback((CreateAndRunWorkflowInstanceRequest request, CancellationToken _) => observedRequest = request)
                    .Returns(new RunWorkflowInstanceResponse());

        await _handler.HandleAsync(command, CancellationToken.None);

        await Assert.That(observedRequest).IsNotNull();
        await Assert.That(observedRequest.Properties).IsNotSameReferenceAs(properties);
        await Assert.That(observedRequest.Properties).IsEquivalentTo(new Dictionary<string, object> { ["WaitForCompletion"] = true }, EqualityComparer<KeyValuePair<string, object>>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(properties.Keys).Contains("__elsa.internal.dispatch-activation-denied.activity-type-name");
        await Assert.That(properties.Keys).Contains("__elsa.internal.dispatch-activation-denied.stimulus-hash");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    public async Task HandleAsync_DoesNotCheckExistingInstance_WhenCommandInstanceIdIsMissing(string? instanceId)
    {
        var command = new DispatchWorkflowDefinitionCommand("definition-version-1")
        {
            InstanceId = instanceId,
            SkipIfInstanceExists = true
        };
        Mock.Get(_workflowRuntime).CreateClientAsync(instanceId, Arg.Any<CancellationToken>()).Returns(_workflowClient);

        await _handler.HandleAsync(command, CancellationToken.None);

        Mock.Get(_workflowClient).InstanceExistsAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_workflowClient).CreateAndRunInstanceAsync(
            Arg.Is<CreateAndRunWorkflowInstanceRequest>(x => x.WorkflowDefinitionHandle.DefinitionVersionId == "definition-version-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }
}
