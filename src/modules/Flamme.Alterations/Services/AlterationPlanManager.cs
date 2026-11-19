using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Entities;
using Flamme.Alterations.Core.Enums;
using Flamme.Alterations.Core.Filters;
using Flamme.Alterations.Core.Notifications;
using Flamme.Common;
using Flamme.Mediator.Contracts;

namespace Flamme.Alterations.Services;

/// <inheritdoc />
public class AlterationPlanManager(IAlterationPlanStore planStore, IAlterationJobStore jobStore, TimeProvider timeProvider, INotificationSender notificationSender) : IAlterationPlanManager
{
    /// <inheritdoc />
    public async Task<AlterationPlan?> GetPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        var planFilter = new AlterationPlanFilter { Id = planId };
        return await planStore.FindAsync(planFilter, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> GetIsAllJobsCompletedAsync(string planId, CancellationToken cancellationToken = default)
    {
        // Check if all jobs are completed.
        var jobFilter = new AlterationJobFilter
        {
            PlanId = planId,
            Statuses = [AlterationJobStatus.Pending, AlterationJobStatus.Running]
        };
        
        return await jobStore.CountAsync(jobFilter, cancellationToken) == 0;
    }

    /// <inheritdoc />
    public async Task CompletePlanAsync(AlterationPlan plan, CancellationToken cancellationToken = default)
    {
        plan.Status = AlterationPlanStatus.Completed;
        plan.CompletedAt = timeProvider.GetUtcNow();
        
        await planStore.SaveAsync(plan, cancellationToken);
        await notificationSender.SendAsync(new AlterationPlanCompleted(plan), cancellationToken);
    }
}