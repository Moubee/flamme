using Flamme.Workflows;

namespace Flamme.Testing.Shared;

public class ActivityExecutedEventArgs(ActivityExecutionContext activityExecutionContext) : EventArgs
{
    public ActivityExecutionContext ActivityExecutionContext { get; } = activityExecutionContext;
}