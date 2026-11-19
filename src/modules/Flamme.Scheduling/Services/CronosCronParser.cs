using Cronos;
using Flamme.Common;

namespace Flamme.Scheduling.Services;

/// <summary>
/// A cron parser that uses Cronos.
/// </summary>
public class CronosCronParser : ICronParser
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronosCronParser"/> class.
    /// </summary>
    public CronosCronParser(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }
    
    /// <inheritdoc />
    public DateTimeOffset GetNextOccurrence(string expression)
    {
        var parsedExpression = CronExpression.Parse(expression, CronFormat.IncludeSeconds);
        var now = _timeProvider.GetUtcNow();
        return parsedExpression.GetNextOccurrence(now, TimeZoneInfo.Utc).GetValueOrDefault();
    }
}