using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// An event that is published when an activity execution log record is updated.
/// </summary>
/// <param name="Record">The activity execution record.</param>
public record ActivityExecutionRecordUpdated(ActivityExecutionRecord Record) : INotification;