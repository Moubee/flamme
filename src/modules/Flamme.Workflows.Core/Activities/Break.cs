using System.Runtime.CompilerServices;
using Flamme.Extensions;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Signals;
using JetBrains.Annotations;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Break out of a loop.
/// </summary>
[Activity("Elsa", "Looping", "Break out of a loop.")]
[PublicAPI]
public class Break : CodeActivity, ITerminalNode
{
    /// <inheritdoc />
    public Break([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        // Send a signal to the parent scope to break out of the loop.
        await context.SendSignalAsync(new BreakSignal());
    }
}