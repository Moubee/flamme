using Flamme.Mediator.Contracts;

namespace Flamme.Workflows.Runtime.Notifications;

public record BackgroundActivityExecutionCompleted(ActivityExecutionContext ActivityExecutionContext) : INotification;