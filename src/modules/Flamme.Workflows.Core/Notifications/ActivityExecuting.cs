using Flamme.Mediator.Contracts;

namespace Flamme.Workflows.Notifications;

/// <summary>
/// A notification that is sent when an activity is about to execute.
/// </summary>
/// <param name="ActivityExecutionContext">The activity execution context.</param>
public record ActivityExecuting(ActivityExecutionContext ActivityExecutionContext) : INotification;