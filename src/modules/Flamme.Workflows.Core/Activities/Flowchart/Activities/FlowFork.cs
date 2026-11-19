using System.Runtime.CompilerServices;
using Flamme.Extensions;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;
using Flamme.Workflows.UIHints;
using JetBrains.Annotations;

namespace Flamme.Workflows.Activities.Flowchart.Activities;

/// <summary>
/// Branch execution into multiple branches that will be executed in parallel.
/// </summary>
[Activity("ControlFlow", "Fork", 1, "Branch execution into multiple branches that will be executed in parallel.", "Branching", DisplayName = "Fork (flow)")]
[PublicAPI]
public class FlowFork : Activity
{
    /// <inheritdoc />
    public FlowFork([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <summary>
    /// A list of expected outcomes to handle.
    /// </summary>
    [Input(
        Description = "A list of expected outcomes to handle.",
        UIHint = InputUIHints.DynamicOutcomes
    )]
    public Input<ICollection<string>> Branches { get; set; } = null!;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var outcomes = Branches.GetOrDefault(context)?.ToArray() ?? ["Done"];

        await context.CompleteActivityWithOutcomesAsync(outcomes);
    }
}
