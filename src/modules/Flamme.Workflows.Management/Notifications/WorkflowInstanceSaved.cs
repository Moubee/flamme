using Flamme.Mediator.Contracts;
using Flamme.Workflows.Management.Entities;

namespace Flamme.Workflows.Management.Notifications;

/// <summary>
/// A notification that is sent when a workflow instance is saved.
/// </summary>
/// <param name="WorkflowInstance">The workflow instance.</param>
public record WorkflowInstanceSaved(WorkflowInstance WorkflowInstance) : INotification;