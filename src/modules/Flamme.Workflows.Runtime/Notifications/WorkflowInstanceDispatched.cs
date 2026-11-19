using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.Runtime.Responses;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// A notification that is published when a workflow instance has been dispatched.
/// </summary>
public record WorkflowInstanceDispatched(DispatchWorkflowInstanceRequest Request, DispatchWorkflowResponse Response) : INotification;
