using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// Triggered when a bookmark is being saved.
/// </summary>
/// <param name="Bookmark">The bookmark being saved.</param>
public record BookmarkSaving(StoredBookmark Bookmark) : INotification;