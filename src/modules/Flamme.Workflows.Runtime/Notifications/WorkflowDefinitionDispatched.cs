using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.Runtime.Responses;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// A notification that is published when a workflow definition has been dispatched.
/// </summary>
public record WorkflowDefinitionDispatched(DispatchWorkflowDefinitionRequest Request, DispatchWorkflowResponse Response) : INotification;
