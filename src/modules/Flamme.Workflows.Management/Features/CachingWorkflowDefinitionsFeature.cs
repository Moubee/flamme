using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Workflows.Management.Handlers.Notifications;
using Flamme.Workflows.Management.Services;
using Flamme.Workflows.Management.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Management.Features;

/// <summary>
/// Configures workflow definition caching.
/// </summary>
public class CachingWorkflowDefinitionsFeature : FeatureBase
{
    /// <inheritdoc />
    public CachingWorkflowDefinitionsFeature(IModule module) : base(module)
    {
    }
    
    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddSingleton<IWorkflowDefinitionCacheManager, WorkflowDefinitionCacheManager>();
        Services.Decorate<IWorkflowDefinitionStore, CachingWorkflowDefinitionStore>();
        Services.Decorate<IWorkflowDefinitionService, CachingWorkflowDefinitionService>();
        Services.AddNotificationHandler<EvictWorkflowDefinitionServiceCache>();
    }
}