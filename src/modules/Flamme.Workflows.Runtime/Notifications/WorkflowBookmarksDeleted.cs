using Flamme.Mediator.Contracts;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.Runtime.Notifications;

public record WorkflowBookmarksDeleted(List<Bookmark> Bookmarks) : INotification;