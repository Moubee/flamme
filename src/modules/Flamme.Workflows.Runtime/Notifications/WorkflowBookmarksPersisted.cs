using Flamme.Mediator.Contracts;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// Published when the bookmarks of a workflow instance have been persisted.
/// </summary>
/// <param name="Diff">The bookmarks that were added, removed, or unchanged.</param>
public record WorkflowBookmarksPersisted(Diff<Bookmark> Diff) : INotification;