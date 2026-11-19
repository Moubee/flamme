using Flamme.Mediator.Contracts;
using JetBrains.Annotations;

namespace Flamme.Workflows.Management.Notifications;

/// <summary>
/// A notification that is sent when specific workflow definition versions are about to be deleted.
/// </summary>
/// <param name="Ids">The IDs of the workflow definitions.</param>
[PublicAPI]
public record WorkflowDefinitionVersionsDeleting(ICollection<string> Ids) : INotification;