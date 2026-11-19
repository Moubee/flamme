using Flamme.Mediator.Contracts;
using Flamme.Workflows.Activities;
using Flamme.Workflows.State;

namespace Flamme.Workflows.Notifications;

/// <summary>
/// A domain event that is published when a workflow finishes.
/// </summary>
public record WorkflowFinished(Workflow Workflow, WorkflowState WorkflowState, WorkflowExecutionContext WorkflowExecutionContext) : INotification;