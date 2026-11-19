using Flamme.Mediator.Contracts;

namespace Flamme.Workflows.Runtime.Notifications;

public record WorkflowCancelled(string WorkflowInstanceId) : INotification;