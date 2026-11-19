using Flamme.Mediator.Contracts;

namespace Flamme.Workflows.Notifications;

public record InvokingActivityCallback(ActivityExecutionContext Parent, ActivityExecutionContext Child) : INotification;