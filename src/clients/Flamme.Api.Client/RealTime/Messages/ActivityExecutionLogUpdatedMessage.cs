using Flamme.Api.Client.Resources.ActivityExecutions.Models;

namespace Flamme.Api.Client.RealTime.Messages;

/// <summary>
/// Contains information about the activity execution logs updated event.
/// </summary>
/// <param name="Stats">Execution stats about a set of activities.</param>
public record ActivityExecutionLogUpdatedMessage(ICollection<ActivityExecutionStats> Stats);