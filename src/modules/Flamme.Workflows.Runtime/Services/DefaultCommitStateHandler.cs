using System.Runtime.ExceptionServices;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.CommitStates;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Notifications;
using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.State;

namespace Flamme.Workflows.Runtime;

public class DefaultCommitStateHandler(
    IWorkflowInstanceManager workflowInstanceManager,
    IBookmarksPersister bookmarkPersister,
    IVariablePersistenceManager variablePersistenceManager,
    IWorkflowCommitTransaction workflowCommitTransaction,
    IWorkflowCommitNotificationBuffer workflowCommitNotificationBuffer,
    INotificationSender notificationSender,
    ILogRecordSink<ActivityExecutionRecord> activityExecutionLogRecordSink,
    ILogRecordSink<WorkflowExecutionLogRecord> workflowExecutionLogRecordSink) : ICommitStateHandler
{
    public async Task CommitAsync(WorkflowExecutionContext workflowExecutionContext, CancellationToken cancellationToken = default)
    {
        var workflowState = workflowInstanceManager.ExtractWorkflowState(workflowExecutionContext);
        await CommitAsync(workflowExecutionContext, workflowState, cancellationToken);
    }

    public async Task CommitAsync(WorkflowExecutionContext workflowExecutionContext, WorkflowState workflowState, CancellationToken cancellationToken = default)
    {
        WorkflowInstance? workflowInstance = null;
        using var notificationScope = workflowCommitNotificationBuffer.Begin();
        await workflowCommitTransaction.ExecuteAsync(async ct =>
        {
            var updateBookmarksRequest = new UpdateBookmarksRequest(workflowExecutionContext, workflowExecutionContext.BookmarksDiff, workflowExecutionContext.CorrelationId);
            await bookmarkPersister.PersistBookmarksAsync(updateBookmarksRequest);
            await activityExecutionLogRecordSink.PersistExecutionLogsAsync(workflowExecutionContext, ct);
            await workflowExecutionLogRecordSink.PersistExecutionLogsAsync(workflowExecutionContext, ct);
            await variablePersistenceManager.SaveVariablesAsync(workflowExecutionContext);
            workflowInstance = await workflowInstanceManager.SaveAsync(workflowState, ct);
        }, cancellationToken);

        ClearActivityExecutionContextTaint(workflowExecutionContext);
        workflowExecutionContext.ExecutionLog.Clear();
        workflowExecutionContext.ClearCompletedActivityExecutionContexts();
        ExceptionDispatchInfo? flushException = null;
        try
        {
            await notificationScope.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            flushException = ExceptionDispatchInfo.Capture(ex);
        }

        await workflowExecutionContext.ExecuteDeferredTasksAsync();
        await notificationSender.SendAsync(new WorkflowStateCommitted(workflowExecutionContext, workflowState, workflowInstance!), cancellationToken);
        flushException?.Throw();
    }

    private static void ClearActivityExecutionContextTaint(WorkflowExecutionContext workflowExecutionContext)
    {
        foreach (var activityExecutionContext in workflowExecutionContext.ActivityExecutionContexts.Where(x => x.IsDirty))
            activityExecutionContext.ClearTaint();
    }
}