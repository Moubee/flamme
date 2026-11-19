using Flamme.Caching.Features;
using Flamme.Features.Services;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

/// <summary>
/// Provides methods to install and configure the distributed caching feature.
/// </summary>
public static class ModuleExtensions
{
    /// <summary>
    /// Adds the <see cref="MemoryCacheFeature"/> feature to the system.
    /// </summary>
    public static MemoryCacheFeature UseMemoryCache(this IModule module, Action<MemoryCacheFeature>? configure = default)
    {
        return module.Configure(configure);
    }
}