namespace Flamme.Workflows;

public interface IActivityPropertyDefaultValueProvider
{
    /// <summary>
    /// Returns a default value for the specified CLR member.
    /// </summary>
    object? GetDefaultValue(ActivityPropertyDefaultValueContext property);
}
