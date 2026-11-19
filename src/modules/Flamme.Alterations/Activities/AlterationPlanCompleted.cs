using System.ComponentModel;
using System.Runtime.CompilerServices;
using Flamme.Alterations.Bookmarks;
using Flamme.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;

namespace Flamme.Alterations.Activities;

/// <summary>
/// Submits an alteration plan for execution.
/// </summary>
[Browsable(false)]
[Activity("Alterations", "PlanCompleted", 1, "Triggered when an Alteration Plan completed", "Alterations")]
public class AlterationPlanCompleted : Trigger
{
    /// <inheritdoc />
    public AlterationPlanCompleted(Variable<string> planId, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
        PlanId = new Input<string>(planId);
    }

    /// <inheritdoc />
    public AlterationPlanCompleted([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <summary>
    /// The ID of the alteration plan.
    /// </summary>
    public Input<string> PlanId { get; set; } = null!;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        if (context.IsTriggerOfWorkflow())
        {
            await context.CompleteActivityAsync();
            return;
        }
        
        var planId = context.Get(PlanId)!;
        var bookmarkPayload = new AlterationPlanCompletedPayload(planId);
        context.CreateBookmark(bookmarkPayload, false);
    }

    /// <inheritdoc />
    protected override object GetTriggerPayload(TriggerIndexingContext context)
    {
        var planId = context.Get(PlanId)!;
        return new AlterationPlanCompletedPayload(planId);
    }
}