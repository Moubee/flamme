using Flamme.Mediator.Contracts;
using Flamme.Workflows.Management.Entities;
using JetBrains.Annotations;

namespace Flamme.Workflows.Management.Notifications;

/// <summary>
/// A notification that is sent when a workflow definition is about to be published.
/// </summary>
/// <param name="WorkflowDefinition">The workflow definition.</param>
[PublicAPI]
public record WorkflowDefinitionPublishing(WorkflowDefinition WorkflowDefinition) : INotification;