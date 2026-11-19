using Microsoft.Extensions.Logging;

namespace Flamme.Common.RecurringTasks;

public interface ISchedule
{
    ScheduledTimer CreateTimer(Func<Task> action, ILogger? logger = null);
}