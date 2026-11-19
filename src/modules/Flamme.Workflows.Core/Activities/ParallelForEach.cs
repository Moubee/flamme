using System.Runtime.CompilerServices;
using Flamme.Workflows.Attributes;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Schedule an activity for each item in parallel.
/// </summary>
[Activity("ControlFlow", "ParallelForEach", 1, "Schedule an activity for each item in parallel.", "Looping")]
public class ParallelForEach : ParallelForEach<object>
{
    /// <inheritdoc />
    public ParallelForEach([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }
}