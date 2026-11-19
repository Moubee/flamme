using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// Triggered when bookmarks are being deleted.
/// </summary>
/// <param name="Bookmarks">The bookmarks being deleted.</param>
public record BookmarksDeleting(ICollection<StoredBookmark> Bookmarks) : INotification;