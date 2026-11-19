using Flamme.Common.Multitenancy;
using Flamme.Mediator;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Commands;
using Flamme.Workflows.Runtime.Requests;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Services;

public class BackgroundWorkflowDispatcherTests
{
    private readonly ICommandSender _commandSender = ICommandSender.Mock().Object;
    private readonly INotificationSender _notificationSender = INotificationSender.Mock().Object;
    private readonly ITenantAccessor _tenantAccessor = ITenantAccessor.Mock().Object;

    [Test]
    public async Task DispatchWorkflowDefinition_DoesNotPropagateCallerToken()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchWorkflowDefinitionRequest("test-definition-id");
        using var callerCts = new CancellationTokenSource();

        // Act
        await dispatcher.DispatchAsync(request, cancellationToken: callerCts.Token);

        // Assert
        Mock.Get(_commandSender).SendAsync<global::Flamme.Mediator.Models.Unit>(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand<global::Flamme.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DispatchWorkflowInstance_DoesNotPropagateCallerToken()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchWorkflowInstanceRequest("test-instance-id");
        using var callerCts = new CancellationTokenSource();

        // Act
        await dispatcher.DispatchAsync(request, cancellationToken: callerCts.Token);

        // Assert
        Mock.Get(_commandSender).SendAsync<global::Flamme.Mediator.Models.Unit>(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand<global::Flamme.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowInstanceCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DispatchTriggerWorkflows_DoesNotPropagateCallerToken()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchTriggerWorkflowsRequest("TestActivityType", new { });
        using var callerCts = new CancellationTokenSource();

        // Act
        await dispatcher.DispatchAsync(request, cancellationToken: callerCts.Token);

        // Assert
        Mock.Get(_commandSender).SendAsync<global::Flamme.Mediator.Models.Unit>(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand<global::Flamme.Mediator.Models.Unit>>(__value => __value is DispatchTriggerWorkflowsCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DispatchResumeWorkflows_DoesNotPropagateCallerToken()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchResumeWorkflowsRequest("TestActivityType", new { });
        using var callerCts = new CancellationTokenSource();

        // Act
        await dispatcher.DispatchAsync(request, cancellationToken: callerCts.Token);

        // Assert
        Mock.Get(_commandSender).SendAsync<global::Flamme.Mediator.Models.Unit>(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand<global::Flamme.Mediator.Models.Unit>>(__value => __value is DispatchResumeWorkflowsCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    private BackgroundWorkflowDispatcher CreateDispatcher()
    {
        return new(_commandSender, _notificationSender, _tenantAccessor);
    }
}
