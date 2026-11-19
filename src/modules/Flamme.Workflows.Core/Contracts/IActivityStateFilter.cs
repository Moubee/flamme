namespace Flamme.Workflows;

public interface IActivityStateFilter
{
    Task<ActivityStateFilterResult> ExecuteAsync(ActivityStateFilterContext context);
}