using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.Runtime.Responses;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// Refreshes all workflows by re-indexing their triggers.
/// </summary>
public interface IWorkflowDefinitionsRefresher
{
    /// <summary>
    /// Refreshes all workflows by re-indexing their triggers.
    /// </summary>
    Task<RefreshWorkflowDefinitionsResponse> RefreshWorkflowDefinitionsAsync(RefreshWorkflowDefinitionsRequest request, CancellationToken cancellationToken = default);
}