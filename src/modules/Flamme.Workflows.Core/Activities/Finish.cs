using System.Runtime.CompilerServices;
using Flamme.Workflows.Attributes;
using JetBrains.Annotations;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Mark the workflow as finished.
/// </summary>
[Activity("ControlFlow", "Finish", 1, "Mark the workflow as finished.", "Primitives")]
[PublicAPI]
public class Finish : CodeActivity, ITerminalNode
{
    /// <inheritdoc />
    public Finish([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <inheritdoc />
    protected override void Execute(ActivityExecutionContext context)
    {
        context.WorkflowExecutionContext.ClearCompletionCallbacks();
        context.WorkflowExecutionContext.Scheduler.Clear();
        context.WorkflowExecutionContext.Bookmarks.Clear();
        context.WorkflowExecutionContext.TransitionTo(WorkflowSubStatus.Finished);
    }
}