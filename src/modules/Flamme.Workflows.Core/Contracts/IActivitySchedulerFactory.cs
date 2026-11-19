namespace Flamme.Workflows;

public interface IActivitySchedulerFactory
{
    IActivityScheduler CreateScheduler();
}