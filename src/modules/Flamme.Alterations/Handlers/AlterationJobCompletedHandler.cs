using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Notifications;
using Flamme.Mediator.Contracts;
using JetBrains.Annotations;

namespace Flamme.Alterations.Handlers;

/// <summary>
/// Handles <see cref="AlterationJobCompleted"/> notifications and updates the plan status if all jobs are completed.
/// </summary>
[UsedImplicitly]
public class AlterationJobCompletedHandler(IAlterationPlanManager manager) : INotificationHandler<AlterationJobCompleted>
{
    /// <inheritdoc />
    public async Task HandleAsync(AlterationJobCompleted notification, CancellationToken cancellationToken)
    {
        var job = notification.Job;
        var planId = job.PlanId;
        var plan = (await manager.GetPlanAsync(planId, cancellationToken))!;
        var allJobsCompleted = await manager.GetIsAllJobsCompletedAsync(planId, cancellationToken);
        
        if(!allJobsCompleted)
            return;
        
        await manager.CompletePlanAsync(plan, cancellationToken);
    }
}