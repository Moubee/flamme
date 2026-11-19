using System.Runtime.CompilerServices;
using Flamme.Workflows.Activities.Flowchart.Contracts;
using Flamme.Workflows.Activities.Flowchart.Extensions;
using Flamme.Workflows.Activities.Flowchart.Models;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;
using Flamme.Workflows.UIHints;
using JetBrains.Annotations;

namespace Flamme.Workflows.Activities.Flowchart.Activities;

/// <summary>
/// Merge multiple branches into a single branch of execution.
/// Note that this activity is no longer necessary for either AND or OR merges, because all activities inherit the Join Kind property.
/// Use this activity if an explicit join step is desired.
/// </summary>
[Activity("ControlFlow", "Join", 1, "Explicitly merge multiple branches into a single branch of execution.", "Branching", DisplayName = "Join")]
[UsedImplicitly]
public class FlowJoin : Activity, IJoinNode
{
    /// <inheritdoc />
    public FlowJoin([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <summary>
    /// The join mode determines whether this activity should continue as soon as one inbound path comes in (Wait Any), or once all inbound paths have executed (Wait All).
    /// </summary>
    [Input(
        Description = "The join mode determines whether this activity should continue as soon as one inbound path comes in (WaitAny), or once all inbound paths have executed (WaitAll). To wait for all activated inbound paths, set the mode to WaitAllActive.",
        DefaultValue = FlowJoinMode.WaitAny,
        UIHint = InputUIHints.DropDown
    )]
    public Input<FlowJoinMode> Mode { get; set; } = new(FlowJoinMode.WaitAny);

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var parent = context.ParentActivityExecutionContext;
        if (parent != null && !Flowchart.UsesTokenFlow(parent))
            await parent.CancelInboundAncestorsAsync(this);

        // The join behavior is handled by Flowchart, so we can simply complete the activity here.
        await context.CompleteActivityAsync();
    }
}