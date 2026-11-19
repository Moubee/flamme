namespace Flamme.Scheduling.Bookmarks;

public record CronBookmarkPayload(DateTimeOffset ExecuteAt, string CronExpression);