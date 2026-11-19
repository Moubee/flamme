using Cronos;
using Microsoft.Extensions.Logging;

namespace Elsa.Common.RecurringTasks;

public class CronSchedule(TimeProvider timeProvider, CronExpression expression) : ISchedule
{
    public ScheduledTimer CreateTimer(Func<Task> action, ILogger? logger = null)
    {
        return new ScheduledTimer(action, () => expression.GetNextOccurrence(timeProvider.GetUtcNow().UtcDateTime)!.Value - timeProvider.GetUtcNow().UtcDateTime, logger);
    }
}