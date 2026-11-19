using Flamme.Mediator.Contracts;
using JetBrains.Annotations;

namespace Flamme.Workflows.Management.Notifications;

/// <summary>
/// A notification that is sent when workflow definitions are deleted.
/// </summary>
/// <param name="DefinitionIds">The IDs of the workflow definitions.</param>
[PublicAPI]
public record WorkflowDefinitionsDeleted(ICollection<string> DefinitionIds) : INotification;