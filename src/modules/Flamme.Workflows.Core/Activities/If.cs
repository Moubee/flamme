using System.Runtime.CompilerServices;
using Flamme.Expressions.Models;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;
using Flamme.Workflows.UIHints;
using JetBrains.Annotations;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Evaluate a Boolean condition to determine which activity to execute next.
/// </summary>
[Activity("ControlFlow", "If", 1, "Evaluate a Boolean condition to determine which activity to execute next.", "Branching")]
[PublicAPI]
public class If : Activity<bool>
{
    /// <inheritdoc />
    public If([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <inheritdoc />
    public If(Input<bool> condition, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(source, line)
    {
        Condition = condition;
    }

    /// <inheritdoc />
    public If(Func<ExpressionExecutionContext, bool> condition, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(source, line)
    {
        Condition = new Input<bool>(condition);
    }

    /// <inheritdoc />
    public If(Func<bool> condition, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(source, line)
    {
        Condition = new Input<bool>(condition);
    }

    /// <summary>
    /// The condition to evaluate.
    /// </summary>
    [Input(UIHint = InputUIHints.SingleLine)]
    public Input<bool> Condition { get; set; } = new(new Literal<bool>(false));

    /// <summary>
    /// The activity to execute when the condition evaluates to true.
    /// </summary>
    [Port]
    public IActivity? Then { get; set; }

    /// <summary>
    /// The activity to execute when the condition evaluates to false.
    /// </summary>
    [Port]
    public IActivity? Else { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var result = context.Get(Condition);
        var nextActivity = result ? Then : Else;

        context.Set(Result, result);
        await context.ScheduleActivityAsync(nextActivity, OnChildCompleted);
    }

    private async ValueTask OnChildCompleted(ActivityCompletedContext context)
    {
        await context.TargetContext.CompleteActivityAsync();
    }
}