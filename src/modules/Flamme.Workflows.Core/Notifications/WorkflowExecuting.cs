using Flamme.Mediator.Contracts;
using Flamme.Workflows.Activities;

namespace Flamme.Workflows.Notifications;

/// <summary>
/// A domain event that is published before an execution cycle begins.
/// </summary>
public record WorkflowExecuting(Workflow Workflow, WorkflowExecutionContext WorkflowExecutionContext) : INotification;