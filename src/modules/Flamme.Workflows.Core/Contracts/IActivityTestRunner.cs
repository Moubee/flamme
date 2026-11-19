using Flamme.Workflows.Models;

namespace Flamme.Workflows;

public interface IActivityTestRunner
{
    Task<ActivityExecutionContext> RunAsync(WorkflowGraph workflowGraph, IActivity activity, CancellationToken cancellationToken = default);
}