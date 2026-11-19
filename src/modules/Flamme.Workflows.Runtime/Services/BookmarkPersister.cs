using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Notifications;
using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Workflows.Runtime;

/// <inheritdoc />
public class BookmarksPersister(IBookmarkUpdater bookmarkUpdater, INotificationSender notificationSender) : IBookmarksPersister
{
    /// <inheritdoc />
    public async Task PersistBookmarksAsync(UpdateBookmarksRequest updateBookmarksRequest)
    {
        await bookmarkUpdater.UpdateBookmarksAsync(updateBookmarksRequest);

        // Publish domain event.
        await notificationSender.SendAsync(new WorkflowBookmarksIndexed(new(
            updateBookmarksRequest.WorkflowExecutionContext,
            updateBookmarksRequest.Diff.Added, 
            updateBookmarksRequest.Diff.Removed, 
            updateBookmarksRequest.Diff.Unchanged)));

        // Publish domain event.
        await notificationSender.SendAsync(new WorkflowBookmarksPersisted(updateBookmarksRequest.Diff));
    }
}