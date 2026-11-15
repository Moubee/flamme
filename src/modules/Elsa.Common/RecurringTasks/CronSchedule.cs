using Cronos;
using Microsoft.Extensions.Logging;

namespace Elsa.Common.RecurringTasks;

public class CronSchedule(ISystemClock systemClock, CronExpression expression) : ISchedule
{
    public ScheduledTimer CreateTimer(Func<Task> action, ILogger? logger = null)
    {
        return new ScheduledTimer(action, () => expression.GetNextOccurrence(systemClock.UtcNow.UtcDateTime)!.Value - systemClock.UtcNow.UtcDateTime, logger);
    }
}