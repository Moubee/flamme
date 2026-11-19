using Flamme.Mediator.Contracts;

namespace Flamme.Workflows.Runtime.Notifications;

public record WorkflowCancelling(string WorkflowInstanceId) : INotification;