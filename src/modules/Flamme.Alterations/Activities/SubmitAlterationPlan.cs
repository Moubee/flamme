using System.ComponentModel;
using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Entities;
using Flamme.Alterations.Core.Enums;
using Flamme.Alterations.Core.Models;
using Flamme.Common;
using Flamme.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;

namespace Flamme.Alterations.Activities;

/// <summary>
/// Submits an alteration plan for execution.
/// </summary>
[Browsable(false)]
[Output(
    DisplayName = "Plan ID",
    Description = "The ID of the submitted alteration plan."
)]
[Activity("Elsa", "Alterations", "Submits an Alteration Plan", Kind = ActivityKind.Task)]
public class SubmitAlterationPlan : CodeActivity<string>
{
    /// <summary>
    /// The parameters for the alteration plan to be submitted.
    /// </summary>
    public Input<AlterationPlanParams> Params { get; set; } = null!;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var timeProvider = context.GetRequiredService<TimeProvider>();
        var identityGenerator = context.GetRequiredService<IIdentityGenerator>();
        var now = timeProvider.GetUtcNow();
        var planParams = context.Get(Params)!;

        var plan = new AlterationPlan
        {
            Id = string.IsNullOrWhiteSpace(planParams.Id) ? identityGenerator.GenerateId() : planParams.Id,
            Alterations = planParams.Alterations,
            WorkflowInstanceFilter = planParams.Filter,
            Status = AlterationPlanStatus.Pending,
            CreatedAt = now
        };

        var alterationPlanStore = context.GetRequiredService<IAlterationPlanStore>();
        var cancellationToken = context.CancellationToken;
        await alterationPlanStore.SaveAsync(plan, cancellationToken);
        Result.Set(context, plan.Id);
    }
}