using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Runtime.ActivationValidators;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.ActivationValidators;

public class CorrelationStrategyTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    public async Task Throws_WhenCorrelationIdIsBlank(string? correlationId)
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        var strategy = new CorrelationStrategy(store);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await strategy.GetAllowActivationAsync(new(new Workflow(), correlationId, CancellationToken.None)));

        Mock.Get(store).CountAsync(Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task AllowsActivation_WhenNoRunningInstanceExistsForCorrelation()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(0);
        var strategy = new CorrelationStrategy(store);

        var allowed = await strategy.GetAllowActivationAsync(new(new Workflow(), "order-1", CancellationToken.None));

        await Assert.That(allowed).IsTrue();
        Mock.Get(store).CountAsync(
            Arg.Is<WorkflowInstanceFilter>(filter =>
                filter.DefinitionId == null &&
                filter.CorrelationId == "order-1" &&
                filter.WorkflowStatus == WorkflowStatus.Running), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DeniesActivation_WhenRunningInstanceExistsForCorrelation()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(1);
        var strategy = new CorrelationStrategy(store);

        var allowed = await strategy.GetAllowActivationAsync(new(new Workflow(), "order-1", CancellationToken.None));

        await Assert.That(allowed).IsFalse();
    }
}
