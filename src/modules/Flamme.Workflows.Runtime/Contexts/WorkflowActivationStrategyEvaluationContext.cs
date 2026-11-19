using Flamme.Workflows.Activities;

namespace Flamme.Workflows.Runtime;

public class WorkflowActivationStrategyEvaluationContext
{
    public Workflow Workflow { get; set; } = default!;
    public string? CorrelationId { get; set; }
    public CancellationToken CancellationToken { get; set; }
}