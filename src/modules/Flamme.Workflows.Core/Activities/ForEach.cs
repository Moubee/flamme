using Flamme.Workflows.Attributes;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Iterate over a set of values.
/// </summary>
[Activity("ControlFlow", "ForEach", 1, "Iterate over a set of values.", "Looping")]
public class ForEach : ForEach<object>
{
}