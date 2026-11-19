using Flamme.Mediator.Contracts;
using Flamme.Workflows.Management.Entities;
using JetBrains.Annotations;

namespace Flamme.Workflows.Management.Notifications;

/// <summary>
/// A notification that is sent when a workflow definition has been updated.
/// </summary>
/// <param name="WorkflowDefinition">The workflow definition.</param>
[PublicAPI]
public record WorkflowDefinitionDraftSaved(WorkflowDefinition WorkflowDefinition) : INotification;