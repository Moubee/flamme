using Flamme.Mediator.Contracts;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Models;
using JetBrains.Annotations;

namespace Flamme.Workflows.Management.Notifications;

/// <summary>
/// A notification that is sent when a workflow definition is published.
/// </summary>
/// <param name="WorkflowDefinition">The workflow definition.</param>
/// <param name="AffectedWorkflows">The affected workflows.</param>
[PublicAPI]
public record WorkflowDefinitionPublished(WorkflowDefinition WorkflowDefinition, AffectedWorkflows AffectedWorkflows) : INotification;