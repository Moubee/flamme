using Flamme.Alterations.Core.Models;

namespace Flamme.Alterations.Core.Contracts;

/// <summary>
/// Represents a service that can find workflow instances based on specified filters.
/// </summary>
public interface IWorkflowInstanceFinder
{
    /// <summary>
    /// Finds workflow instances based on the specified filter.
    /// </summary>
    Task<IEnumerable<string>> FindAsync(AlterationWorkflowInstanceFilter filter, CancellationToken cancellationToken = default);
}