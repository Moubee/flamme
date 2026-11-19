using Flamme.Mediator.Contracts;

namespace Flamme.Workflows.Notifications;

/// <summary>
/// A notification that is sent when an activity has completed.
/// </summary>
/// <param name="ActivityExecutionContext">The activity execution context.</param>
public record ActivityCompleted(ActivityExecutionContext ActivityExecutionContext) : INotification;