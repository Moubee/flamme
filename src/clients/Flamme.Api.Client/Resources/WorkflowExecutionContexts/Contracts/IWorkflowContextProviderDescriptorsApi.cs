using Flamme.Api.Client.Resources.WorkflowExecutionContexts.Models;
using Flamme.Api.Client.Shared.Models;
using JetBrains.Annotations;
using Refit;

namespace Flamme.Api.Client.Resources.WorkflowExecutionContexts.Contracts;

/// <summary>
/// Provides workflow context provider descriptors.
/// </summary>
[PublicAPI]
public interface IWorkflowContextProviderDescriptorsApi
{
    /// <summary>
    /// Returns a list of workflow context provider descriptors.
    /// </summary>
    [Get("/workflow-contexts/provider-descriptors")]
    Task<ListResponse<WorkflowContextProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default);
}