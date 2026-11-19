using Flamme.Mediator.Contracts;
using Flamme.Workflows.Activities;
using Flamme.Workflows.State;

namespace Flamme.Workflows.Notifications;

/// <summary>
/// A domain event that is published every time an execution cycle completes.
/// </summary>
public record WorkflowExecuted(Workflow Workflow, WorkflowState WorkflowState, WorkflowExecutionContext WorkflowExecutionContext) : INotification;