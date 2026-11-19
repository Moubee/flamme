using System.Runtime.CompilerServices;
using Flamme.Workflows.Attributes;
using JetBrains.Annotations;

namespace Flamme.Workflows.Activities.Flowchart.Activities;

/// <summary>
/// A simple container that executes the specified activity.
/// </summary>
[Activity("ControlFlow", "Node", 1, "A simple container that executes the specified activity.", "Flow", DisplayName = "Container")]
[PublicAPI]
public class FlowNode : Activity
{
    
    /// <inheritdoc />
    public FlowNode([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }
    
    /// <summary>
    /// The activity to execute.
    /// </summary>
    [Port]
    public IActivity? Body { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context) => await context.ScheduleActivityAsync(Body, OnBodyCompletedAsync);

    private async ValueTask OnBodyCompletedAsync(ActivityCompletedContext context) => await context.TargetContext.CompleteActivityAsync();
}