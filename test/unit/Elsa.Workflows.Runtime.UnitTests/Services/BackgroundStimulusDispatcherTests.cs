using Elsa.Common.Multitenancy;
using Elsa.Mediator;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime.Commands;
using Elsa.Workflows.Runtime.Requests;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class BackgroundStimulusDispatcherTests
{
    private readonly ICommandSender _commandSender = Mock.Of<ICommandSender>().Object;
    private readonly ITenantAccessor _tenantAccessor = Mock.Of<ITenantAccessor>().Object;

    [Test]
    public async Task SendAsync_DoesNotPropagateCallerToken()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var request = new DispatchStimulusRequest();
        using var callerCts = new CancellationTokenSource();

        // Act
        await dispatcher.SendAsync(request, callerCts.Token);

        // Assert - background commands run independently of caller's lifecycle
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchStimulusCommand), CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    private BackgroundStimulusDispatcher CreateDispatcher()
    {
        return new(_commandSender, _tenantAccessor);
    }
}
