using Flamme.Alterations.Bookmarks;
using Flamme.Alterations.Core.Notifications;
using Flamme.Mediator.Contracts;
using Flamme.Workflows;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Runtime;
using JetBrains.Annotations;

namespace Flamme.Alterations.Handlers;

/// <summary>
/// Handles <see cref="AlterationPlanCompleted"/> notifications and triggers any workflows that are waiting for the plan to complete.
/// </summary>
[UsedImplicitly]
public class AlterationPlanCompletedHandler(IBookmarkQueue bookmarkQueue, IStimulusHasher stimulusHasher) : INotificationHandler<AlterationPlanCompleted>
{
    /// <inheritdoc />
    public async Task HandleAsync(AlterationPlanCompleted notification, CancellationToken cancellationToken)
    {
        // Trigger any workflow instances that are waiting for the plan to complete.
        var planId = notification.Plan.Id;
        var bookmarkPayload = new AlterationPlanCompletedPayload(planId);
        var activityTypeName = ActivityTypeNameHelper.GenerateTypeName<Activities.AlterationPlanCompleted>();
        var item = new NewBookmarkQueueItem
        {
            ActivityTypeName = activityTypeName,
            StimulusHash = stimulusHasher.Hash(activityTypeName, bookmarkPayload)
        };
        await bookmarkQueue.EnqueueAsync(item, cancellationToken);
    }
}