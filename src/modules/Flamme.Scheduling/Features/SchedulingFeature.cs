using Flamme.Common.Features;
using Flamme.Common.Multitenancy;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Scheduling.Bookmarks;
using Flamme.Scheduling.Handlers;
using Flamme.Scheduling.Options;
using Flamme.Scheduling.Services;
using Flamme.Scheduling.StartupTasks;
using Flamme.Scheduling.TriggerPayloadValidators;
using Flamme.Workflows.Management.Features;
using Flamme.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;
using Flamme.Common.Serialization;

namespace Flamme.Scheduling.Features;

/// <summary>
/// Provides scheduling features to the system.
/// </summary>
[DependsOn(typeof(TimeProviderFeature))]
public class SchedulingFeature : FeatureBase
{
    /// <inheritdoc />
    public SchedulingFeature(IModule module) : base(module)
    {
    }
    
    /// <summary>
    /// Gets or sets the trigger scheduler.
    /// </summary>
    public Func<IServiceProvider, IWorkflowScheduler> WorkflowScheduler { get; set; } = sp => sp.GetRequiredService<DefaultWorkflowScheduler>();
    
    /// <summary>
    /// Gets or sets the CRON parser.
    /// </summary>
    public Func<IServiceProvider, ICronParser> CronParser { get; set; } = sp => sp.GetRequiredService<CronosCronParser>();

    /// <inheritdoc />
    public override void Apply()
    {
        Services
            .AddSingleton<UpdateTenantSchedules>()
            .AddSingleton<ITenantDeletedEvent>(sp => sp.GetRequiredService<UpdateTenantSchedules>())
            .AddSingleton<IScheduler, LocalScheduler>()
            .AddSingleton<PastDueScheduleStaggerer>()
            .AddSingleton<CronosCronParser>()
            .AddSingleton(CronParser)
            .AddScoped<ITriggerScheduler, DefaultTriggerScheduler>()
            .AddScoped<IBookmarkScheduler, DefaultBookmarkScheduler>()
            .AddScoped<DefaultWorkflowScheduler>()
            .AddScoped(WorkflowScheduler)
            .AddStartupTask<CreateSchedulesStartupTask>()
            .AddHandlersFrom<ScheduleWorkflows>()

            //Trigger payload validators.
            .AddTriggerPayloadValidator<CronTriggerPayloadValidator, CronTriggerPayload>()

            // Graceful shutdown: register scheduled-trigger ingress for diagnostic visibility (FR-006).
            .AddSingleton<Flamme.Workflows.Runtime.IIngressSource, Flamme.Scheduling.IngressSources.ScheduledTriggerIngressSource>();

        Services.Configure<SchedulingOptions>(_ => { });
        Services.Configure<SerializationTypeOptions>(options =>
        {
            options.RegisterTypeAlias(typeof(CronBookmarkPayload), nameof(CronBookmarkPayload));
            options.RegisterTypeAlias(typeof(CronTriggerPayload), nameof(CronTriggerPayload));
            options.RegisterTypeAlias(typeof(DelayPayload), nameof(DelayPayload));
            options.RegisterTypeAlias(typeof(StartAtPayload), nameof(StartAtPayload));
            options.RegisterTypeAlias(typeof(TimerBookmarkPayload), nameof(TimerBookmarkPayload));
            options.RegisterTypeAlias(typeof(TimerTriggerPayload), nameof(TimerTriggerPayload));
        });

        Module.Configure<WorkflowManagementFeature>(management => management.AddActivitiesFrom<SchedulingFeature>());
    }
}
