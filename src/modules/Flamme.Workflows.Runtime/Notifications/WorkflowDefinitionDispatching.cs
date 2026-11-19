using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// A notification that is published when a workflow definition is being dispatched.
/// </summary>
public record WorkflowDefinitionDispatching(DispatchWorkflowDefinitionRequest Request) : INotification;
