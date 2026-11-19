using Flamme.Extensions;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Notifications;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// This implementation saves <see cref="ActivityExecutionRecord"/> directly through the store.
/// </summary>
public class StoreActivityExecutionLogSink(
    IActivityExecutionStore activityExecutionStore,
    INotificationSender notificationSender)
    : ILogRecordSink<ActivityExecutionRecord>
{
    /// <inheritdoc />
    public async Task PersistExecutionLogsAsync(WorkflowExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Select tainted activity execution contexts to avoid saving untainted ones multiple times.
        var activityExecutionContexts = context.ActivityExecutionContexts.Where(x => x.IsDirty).ToList();

        if (activityExecutionContexts.Count == 0)
            return;

        var records = await Task.WhenAll(activityExecutionContexts.Select(x => x.GetOrMapCapturedActivityExecutionRecordAsync()));
        await activityExecutionStore.SaveManyAsync(records, cancellationToken);
        // DefaultCommitStateHandler clears taint after the commit transaction succeeds.
        await notificationSender.SendAsync(new ActivityExecutionLogUpdated(context, records), cancellationToken);
    }
}