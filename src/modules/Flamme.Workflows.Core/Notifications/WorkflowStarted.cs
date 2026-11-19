using Flamme.Mediator.Contracts;
using Flamme.Workflows.Activities;

namespace Flamme.Workflows.Notifications;

/// <summary>
/// A domain event that is published when a workflow starts.
/// </summary>
public record WorkflowStarted(Workflow Workflow, WorkflowExecutionContext WorkflowExecutionContext) : INotification;