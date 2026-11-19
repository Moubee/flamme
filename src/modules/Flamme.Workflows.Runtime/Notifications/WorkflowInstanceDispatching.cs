using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// A notification that is published when a workflow instance is being dispatched.
/// </summary>
public record WorkflowInstanceDispatching(DispatchWorkflowInstanceRequest Request) : INotification;
