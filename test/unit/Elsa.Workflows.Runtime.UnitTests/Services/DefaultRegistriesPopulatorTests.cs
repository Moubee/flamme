using Elsa.Mediator.Contracts;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Runtime.Notifications;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class DefaultRegistriesPopulatorTests
{
    private readonly IWorkflowDefinitionStorePopulator _workflowDefinitionStorePopulatorMock;
    private readonly IActivityRegistryPopulator _activityRegistryPopulatorMock;
    private readonly INotificationSender _notificationSenderMock;
    private readonly DefaultRegistriesPopulator _populator;

    public DefaultRegistriesPopulatorTests()
    {
        _workflowDefinitionStorePopulatorMock = IWorkflowDefinitionStorePopulator.Mock().Object;
        _activityRegistryPopulatorMock = IActivityRegistryPopulator.Mock().Object;
        _notificationSenderMock = INotificationSender.Mock().Object;

        _populator = new(
            _workflowDefinitionStorePopulatorMock,
            _activityRegistryPopulatorMock,
            _notificationSenderMock);
    }

    [Test]
    [DisplayName("PopulateAsync publishes WorkflowDefinitionsReloaded notification")]
    public async Task PopulateAsync_PublishesWorkflowDefinitionsReloadedNotification()
    {
        // Arrange
        var workflowDefinitions = new List<WorkflowDefinition>
        {
            new()
            {
                Id = "1",
                DefinitionId = "def-1",
                Version = 1
            },
            new()
            {
                Id = "2",
                DefinitionId = "def-2",
                Version = 1
            }
        };
        Mock.Get(_workflowDefinitionStorePopulatorMock).PopulateStoreAsync(true, Arg.Any<CancellationToken>())
                    .Returns(workflowDefinitions);

        // Act
        await _populator.PopulateAsync();

        // Assert
        Mock.Get(_notificationSenderMock).SendAsync(
            Arg.Is<global::Elsa.Mediator.Contracts.INotification>(__value => __value is WorkflowDefinitionsReloaded __typed && (__typed.ReloadedWorkflowDefinitions.Count == 2)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task PopulateAsync_EnsuresActivityRegistryBeforeEachWorkflowDefinitionStorePopulation()
    {
        // Arrange
        Mock.Get(_workflowDefinitionStorePopulatorMock).PopulateStoreAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                    .Returns(Array.Empty<WorkflowDefinition>());

        // Act
        await _populator.PopulateAsync();

        // Assert
        Mock.VerifyInOrder(() =>
                {
                    Mock.Get(_activityRegistryPopulatorMock).EnsureRegistryPopulatedAsync(Arg.Any<CancellationToken>()).WasCalled();
                    Mock.Get(_workflowDefinitionStorePopulatorMock).PopulateStoreAsync(false, Arg.Any<CancellationToken>()).WasCalled();
                    Mock.Get(_activityRegistryPopulatorMock).EnsureRegistryPopulatedAsync(Arg.Any<CancellationToken>()).WasCalled();
                    Mock.Get(_workflowDefinitionStorePopulatorMock).PopulateStoreAsync(true, Arg.Any<CancellationToken>()).WasCalled();
                    Mock.Get(_notificationSenderMock).SendAsync(Arg.Is<global::Elsa.Mediator.Contracts.INotification>(__value => __value is WorkflowDefinitionsReloaded), Arg.Any<CancellationToken>()).WasCalled();
                });

        Mock.Get(_activityRegistryPopulatorMock).EnsureRegistryPopulatedAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(2));
    }
}
