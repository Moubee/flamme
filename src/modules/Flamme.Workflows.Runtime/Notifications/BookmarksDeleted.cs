using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// Triggered when bookmarks have been deleted.
/// </summary>
/// <param name="Bookmarks">The bookmarks that have been deleted.</param>
public record BookmarksDeleted(ICollection<StoredBookmark> Bookmarks) : INotification;