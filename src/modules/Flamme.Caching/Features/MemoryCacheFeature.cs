using Flamme.Caching.Options;
using Flamme.Caching.Services;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Caching.Features;

/// <summary>
/// Configures the MemoryCache.
/// </summary>
public class MemoryCacheFeature(IModule module) : FeatureBase(module)
{
    /// <summary>
    /// A delegate to configure the <see cref="CachingOptions"/>.
    /// </summary>
    public Action<CachingOptions> CachingOptions { get; set; } = _ => { };

    /// <inheritdoc />
    public override void Apply()
    {
        Services.Configure(CachingOptions);

        Services
            .AddMemoryCache()
            .AddSingleton<ICacheManager, CacheManager>()
            .AddSingleton<IChangeTokenSignalInvoker, ChangeTokenSignalInvoker>()
            .AddSingleton<IChangeTokenSignaler, ChangeTokenSignaler>()
            ;
    }
}