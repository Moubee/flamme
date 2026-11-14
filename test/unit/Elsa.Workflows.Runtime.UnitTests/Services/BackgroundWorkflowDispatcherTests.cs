using Elsa.Common.Multitenancy;
using Elsa.Mediator;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime.Commands;
using Elsa.Workflows.Runtime.Requests;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class BackgroundWorkflowDispatcherTests
{
    private readonly ICommandSender _commandSender = Mock.Of<ICommandSender>().Object;
    private readonly INotificationSender _notificationSender = Mock.Of<INotificationSender>().Object;
    private readonly ITenantAccessor _tenantAccessor = Mock.Of<ITenantAccessor>().Object;

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
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
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
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowInstanceCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
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
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchTriggerWorkflowsCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
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
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchResumeWorkflowsCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    private BackgroundWorkflowDispatcher CreateDispatcher()
    {
        return new(_commandSender, _notificationSender, _tenantAccessor);
    }
}
