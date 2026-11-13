using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.ActivationValidators;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class DefaultWorkflowActivationStrategyEvaluatorTests
{
    [Test]
    public async Task ThrowsActionableException_WhenConfiguredStrategyIsNotRegistered()
    {
        var evaluator = new DefaultWorkflowActivationStrategyEvaluator([]);
        var workflow = new Workflow
        {
            Options = new WorkflowOptions { ActivationStrategyType = typeof(UnregisteredStrategy) }
        };

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            evaluator.CanStartWorkflowAsync(new()
            {
                Workflow = workflow,
                CorrelationId = "correlation-1",
                CancellationToken = CancellationToken.None
            }));

        await Assert.That(exception.Message).Contains(nameof(UnregisteredStrategy)).WithComparison(StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains(nameof(IWorkflowActivationStrategy)).WithComparison(StringComparison.Ordinal);
    }

    private sealed class UnregisteredStrategy : IWorkflowActivationStrategy
    {
        public ValueTask<bool> GetAllowActivationAsync(WorkflowInstantiationStrategyContext context) => ValueTask.FromResult(true);
    }
}
