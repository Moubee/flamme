using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime.ActivationValidators;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.ActivationValidators;

public class CorrelatedSingletonStrategyTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    public async Task Throws_WhenCorrelationIdIsBlank(string? correlationId)
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        var strategy = new CorrelatedSingletonStrategy(store);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await strategy.GetAllowActivationAsync(new(CreateWorkflow(), correlationId, CancellationToken.None)));

        Mock.Get(store).CountAsync(Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task AllowsActivation_WhenNoRunningInstanceExistsForDefinitionAndCorrelation()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(0);
        var strategy = new CorrelatedSingletonStrategy(store);

        var allowed = await strategy.GetAllowActivationAsync(new(CreateWorkflow("def-1"), "order-1", CancellationToken.None));

        await Assert.That(allowed).IsTrue();
        Mock.Get(store).CountAsync(
            Arg.Is<WorkflowInstanceFilter>(filter =>
                filter.DefinitionId == "def-1" &&
                filter.CorrelationId == "order-1" &&
                filter.WorkflowStatus == WorkflowStatus.Running), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DeniesActivation_WhenRunningInstanceExistsForDefinitionAndCorrelation()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(1);
        var strategy = new CorrelatedSingletonStrategy(store);

        var allowed = await strategy.GetAllowActivationAsync(new(CreateWorkflow(), "order-1", CancellationToken.None));

        await Assert.That(allowed).IsFalse();
    }

    private static Workflow CreateWorkflow(string definitionId = "definition-1")
    {
        return new()
        {
            Identity = new WorkflowIdentity(definitionId, 1, definitionId)
        };
    }
}
