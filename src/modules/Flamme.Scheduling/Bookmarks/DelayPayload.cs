using Flamme.Scheduling.Activities;

namespace Flamme.Scheduling.Bookmarks;

/// <summary>
/// A bookmark payload for <see cref="Delay"/>.
/// </summary>
public record DelayPayload(DateTimeOffset ResumeAt);