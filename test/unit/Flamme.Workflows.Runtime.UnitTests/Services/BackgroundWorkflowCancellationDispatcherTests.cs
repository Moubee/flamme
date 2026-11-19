using Flamme.Common.Multitenancy;
using Flamme.Mediator;
using Flamme.Mediator.Contracts;
using Flamme.Mediator.Options;
using Flamme.Tenants.Mediator;
using Flamme.Workflows.Runtime.Commands;
using Flamme.Workflows.Runtime.Requests;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Services;

public class BackgroundWorkflowCancellationDispatcherTests
{
    private readonly ICommandSender _commandSender = ICommandSender.Mock().Object;
    private readonly ITenantAccessor _tenantAccessor = ITenantAccessor.Mock().Object;

    [Test]
    public async Task DispatchAsync_SendsCommandWithBackgroundStrategy()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchCancelWorkflowRequest();

        // Act
        await dispatcher.DispatchAsync(request);

        // Assert
        Mock.Get(_commandSender).SendAsync(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand>(__value => __value is CancelWorkflowsCommand __typed && (__typed.Request == request)), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DispatchAsync_IncludesTenantIdInHeaders_WhenTenantIsPresent()
    {
        // Arrange
        var tenantId = "test-tenant-123";
        var tenant = new Tenant
        {
            Id = tenantId
        };
        Mock.Get(_tenantAccessor).Tenant.Returns(tenant);

        var dispatcher = CreateDispatcher();
        var request = new DispatchCancelWorkflowRequest();

        // Act
        await dispatcher.DispatchAsync(request);

        // Assert
        Mock.Get(_commandSender).SendAsync(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand>(__value => __value is CancelWorkflowsCommand), CommandStrategy.Background, Arg.Is<IDictionary<object, object>>(headers =>
                headers.ContainsKey(TenantHeaders.TenantIdKey) &&
                headers[TenantHeaders.TenantIdKey].ToString() == tenantId), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DispatchAsync_DoesNotIncludeTenantIdInHeaders_WhenTenantIsNull()
    {
        // Arrange
        Mock.Get(_tenantAccessor).Tenant.Returns((Tenant?)null);

        var dispatcher = CreateDispatcher();
        var request = new DispatchCancelWorkflowRequest();

        // Act
        await dispatcher.DispatchAsync(request);

        // Assert
        Mock.Get(_commandSender).SendAsync(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand>(__value => __value is CancelWorkflowsCommand), CommandStrategy.Background, Arg.Is<IDictionary<object, object>>(headers =>
                !headers.ContainsKey(TenantHeaders.TenantIdKey)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DispatchAsync_ReturnsResponse()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchCancelWorkflowRequest();

        // Act
        var response = await dispatcher.DispatchAsync(request);

        // Assert
        await Assert.That(response).IsNotNull();
    }

    [Test]
    public async Task DispatchAsync_DoesNotPropagateCallerToken()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchCancelWorkflowRequest();
        using var callerCts = new CancellationTokenSource();

        // Act
        await dispatcher.DispatchAsync(request, callerCts.Token);

        // Assert - background commands run independently of caller's lifecycle
        Mock.Get(_commandSender).SendAsync(
            Arg.Is<global::Flamme.Mediator.Contracts.ICommand>(__value => __value is CancelWorkflowsCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    private BackgroundWorkflowCancellationDispatcher CreateDispatcher()
    {
        return new(_commandSender, _tenantAccessor);
    }
}
