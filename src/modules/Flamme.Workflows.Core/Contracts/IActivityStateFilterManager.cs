namespace Flamme.Workflows;

public interface IActivityStateFilterManager
{
    Task<string> RunFiltersAsync(ActivityStateFilterContext context);
}