using Flamme.Alterations.Core.Models;

namespace Flamme.Alterations.Core.Contracts;

/// <summary>
/// Scheduler for alteration plans.
/// </summary>
public interface IAlterationPlanScheduler
{
    /// <summary>
    /// Submits an alteration plan for execution.
    /// </summary>
    Task<string> SubmitAsync(AlterationPlanParams planParams, CancellationToken cancellationToken = default);
}