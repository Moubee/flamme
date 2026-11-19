namespace Flamme.Scheduling.Bookmarks;

public record TimerTriggerPayload(DateTimeOffset StartAt, TimeSpan Interval);