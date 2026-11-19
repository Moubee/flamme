using System.Runtime.CompilerServices;
using Flamme.Workflows.Attributes;
using JetBrains.Annotations;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Marks the end of a flowchart, causing the flowchart to complete.
/// </summary>
[Activity("ControlFlow", "End", 1, "A milestone activity that marks the end of a flowchart.", "Flow", Kind = ActivityKind.Action)]
[PublicAPI]
public class End : CodeActivity, ITerminalNode
{
    /// <inheritdoc />
    public End([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }
}