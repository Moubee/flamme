using Flamme.Api.Client.Resources.WorkflowActivationStrategies.Models;
using Flamme.Api.Client.Shared.Models;
using Refit;

namespace Flamme.Api.Client.Resources.WorkflowActivationStrategies.Contracts;

/// <summary>
/// Represents a client for the variable types API.
/// </summary>
public interface IWorkflowActivationStrategiesApi
{
    /// <summary>
    /// Lists workflow activation strategies.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    [Get("/descriptors/workflow-activation-strategies")]
    Task<ListResponse<WorkflowActivationStrategyDescriptor>> ListAsync(CancellationToken cancellationToken = default);
}