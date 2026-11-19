using Flamme.Workflows.Attributes;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Iterate over a set of values.
/// </summary>
[Activity("Elsa", "Looping", "Iterate over a set of values.")]
public class ForEach : ForEach<object>
{
}