using Flamme.Api.Client.Resources.ActivityExecutions.Models;

namespace Flamme.Api.Client.RealTime.Messages;

/// <summary>
/// Contains information about the activity executing event.
/// </summary>
/// <param name="ActivityId">The ID of the activity.</param>
/// <param name="Stats">Execution stats about the activity.</param>
public record ActivityExecutingMessage(string ActivityId, ActivityExecutionStats Stats);