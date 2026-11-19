using System.ComponentModel;
using System.Runtime.CompilerServices;
using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Entities;
using Flamme.Alterations.Core.Enums;
using Flamme.Alterations.Core.Filters;
using Flamme.Alterations.Core.Models;
using Flamme.Common;
using Flamme.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Exceptions;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;

namespace Flamme.Alterations.Activities;

/// <summary>
/// Submits an alteration plan for execution.
/// </summary>
[Browsable(false)]
[Activity("Alterations", "GenerateJobs", 1, "Generates jobs for the specified Alteration Plan", "Alterations", Kind = ActivityKind.Task)]
public class GenerateAlterationJobs : CodeActivity<int>
{
    /// <inheritdoc />
    public GenerateAlterationJobs([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <inheritdoc />
    public GenerateAlterationJobs(Variable<string> planId, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
        PlanId = new Input<string>(planId);
    }

    /// <summary>
    /// The ID of the submitted alteration plan.
    /// </summary>
    public Input<string> PlanId { get; set; } = null!;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var plan = await GetPlanAsync(context);
        await UpdatePlanStatusAsync(context, plan);
        var workflowInstanceIds = (await FindMatchingWorkflowInstanceIdsAsync(context, plan.WorkflowInstanceFilter)).ToList();

        if (workflowInstanceIds.Any())
            await GenerateJobsAsync(context, plan, workflowInstanceIds);

        context.SetResult(workflowInstanceIds.Count);
    }

    private async Task<AlterationPlan> GetPlanAsync(ActivityExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var planId = context.Get(PlanId)!;
        var alterationPlanStore = context.GetRequiredService<IAlterationPlanStore>();
        var planFilter = new AlterationPlanFilter
        {
            Id = planId
        };
        var plan = await alterationPlanStore.FindAsync(planFilter, cancellationToken);

        if (plan == null)
            throw new FaultException(AlterationFaultCodes.PlanNotFound, AlterationFaultCategories.Alteration, DefaultFaultTypes.System, $"Alteration Plan with ID {planId} not found.");

        return plan;
    }

    private async Task UpdatePlanStatusAsync(ActivityExecutionContext context, AlterationPlan plan)
    {
        var cancellationToken = context.CancellationToken;
        var alterationPlanStore = context.GetRequiredService<IAlterationPlanStore>();
        plan.Status = AlterationPlanStatus.Generating;
        await alterationPlanStore.SaveAsync(plan, cancellationToken);
    }

    private async Task<IEnumerable<string>> FindMatchingWorkflowInstanceIdsAsync(ActivityExecutionContext context, AlterationWorkflowInstanceFilter filter)
    {
        var cancellationToken = context.CancellationToken;
        var workflowInstanceFinder = context.GetRequiredService<IWorkflowInstanceFinder>();
        return await workflowInstanceFinder.FindAsync(filter, cancellationToken);
    }

    private async Task GenerateJobsAsync(ActivityExecutionContext context, AlterationPlan plan, IEnumerable<string> workflowInstanceIds)
    {
        var cancellationToken = context.CancellationToken;
        var identityGenerator = context.GetRequiredService<IIdentityGenerator>();
        var timeProvider = context.GetRequiredService<TimeProvider>();
        var jobs = workflowInstanceIds.Select(workflowInstanceId => new AlterationJob
            {
                Id = identityGenerator.GenerateId(),
                PlanId = plan.Id,
                Status = AlterationJobStatus.Pending,
                WorkflowInstanceId = workflowInstanceId,
                CreatedAt = timeProvider.GetUtcNow()
            })
            .ToList();

        var alterationJobStore = context.GetRequiredService<IAlterationJobStore>();
        await alterationJobStore.SaveManyAsync(jobs, cancellationToken);
    }
}