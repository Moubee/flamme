using Flamme.Common;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Resilience.Features;
using Flamme.Workflows.Runtime.Distributed.StartupTasks;
using Flamme.Workflows.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flamme.Workflows.Runtime.Distributed.Features;

/// <summary>
/// Installs and configures workflow runtime features.
/// </summary>
[DependsOn(typeof(WorkflowRuntimeFeature))]
[DependsOn(typeof(ResilienceFeature))]
public class DistributedRuntimeFeature(IModule module) : FeatureBase(module)
{
    public override void Configure()
    {
        Module.UseWorkflowRuntime(runtime =>
        {
            runtime.WorkflowRuntime = sp => sp.GetRequiredService<DistributedWorkflowRuntime>();
            runtime.BookmarkQueueWorker = sp => sp.GetRequiredService<DistributedBookmarkQueueWorker>();
        });
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services
            .AddScoped<DistributedWorkflowRuntime>()
            .AddScoped<DistributedBookmarkQueueWorker>()

            .Decorate<IWorkflowDefinitionsRefresher, DistributedWorkflowDefinitionsRefresher>()
            .Decorate<IWorkflowDefinitionsReloader, DistributedWorkflowDefinitionsReloader>();

        Services.TryAddScoped<DistributedRuntimeLockProviderValidator>();
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateDistributedRuntimeLockProviderStartupTask>());
    }
}
