using Flamme.Caching.Features;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Workflows.Runtime.Handlers;
using Flamme.Workflows.Runtime.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Runtime.Features;

/// <summary>
/// Installs and configures workflow runtime caching features.
/// </summary>
[DependsOn(typeof(MemoryCacheFeature))]
public class CachingWorkflowRuntimeFeature : FeatureBase
{
    /// <inheritdoc />
    public CachingWorkflowRuntimeFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services
            // Decorators.
            .Decorate<ITriggerStore, CachingTriggerStore>()

            // Handlers.
            .AddNotificationHandler<InvalidateTriggersCache>()
            .AddNotificationHandler<InvalidateWorkflowsCache>();
    }
}