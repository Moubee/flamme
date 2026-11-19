using Flamme.Mediator.Contracts;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.State;

namespace Flamme.Workflows.Runtime.Notifications;

public record WorkflowStateCommitted(WorkflowExecutionContext WorkflowExecutionContext, WorkflowState WorkflowState, WorkflowInstance WorkflowInstance) : INotification;