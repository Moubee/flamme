using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Http.Handlers;
using Flamme.Http.Services;
using Flamme.Workflows.Management.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Http.Features;

/// <summary>
/// Installs services related to HTTP caching.
/// </summary>
[DependsOn(typeof(HttpFeature))]
[DependsOn(typeof(CachingWorkflowDefinitionsFeature))]
public class HttpCacheFeature : FeatureBase
{
    /// <inheritdoc />
    public HttpCacheFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services
            .AddSingleton<IHttpWorkflowsCacheManager, HttpWorkflowsCacheManager>()
            .Decorate<IHttpWorkflowLookupService, CachingHttpWorkflowLookupService>()
            .AddNotificationHandler<InvalidateHttpWorkflowsCache>();
    }
}