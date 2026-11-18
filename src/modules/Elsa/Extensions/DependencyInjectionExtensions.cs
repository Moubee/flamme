using System.Runtime.CompilerServices;
using Elsa.Features;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Extensions;

/// <summary>
/// Provides extension methods to <see cref="IServiceCollection"/>.
/// </summary>
public static class ModuleExtensions
{
    private static readonly ConditionalWeakTable<IServiceCollection, CachedModule> Modules = new();

    /// <summary>
    /// Adds the Elsa features using managed discovery and applies the accumulated configuration.
    /// </summary>
    public static IModule AddElsa(this IServiceCollection services, Action<IModule>? configure = null)
        => AddElsa(GetOrCreateModule(services, null), configure);

    /// <summary>
    /// Adds the Elsa features using the supplied catalog and applies the accumulated configuration.
    /// </summary>
    public static IModule AddElsa(this IServiceCollection services, Action<IModule>? configure, ExplicitFeatureCatalog catalog)
        => AddElsa(GetOrCreateModule(services, catalog ?? throw new ArgumentNullException(nameof(catalog))), configure);

    /// <summary>
    /// Accumulates configuration for the Elsa module using managed discovery.
    /// </summary>
    public static IModule ConfigureElsa(this IServiceCollection services, Action<IModule>? configure = null)
        => ConfigureElsa(GetOrCreateModule(services, null), configure);

    /// <summary>
    /// Accumulates configuration for the Elsa module using the supplied catalog.
    /// </summary>
    public static IModule ConfigureElsa(this IServiceCollection services, Action<IModule>? configure, ExplicitFeatureCatalog catalog)
        => ConfigureElsa(GetOrCreateModule(services, catalog ?? throw new ArgumentNullException(nameof(catalog))), configure);

    private static IModule AddElsa(CachedModule cached, Action<IModule>? configure)
    {
        lock (cached)
        {
            cached.Module.Configure<AppFeature>(app => app.Configurator += configure);
            cached.Module.Apply();
            return cached.Module;
        }
    }

    private static IModule ConfigureElsa(CachedModule cached, Action<IModule>? configure)
    {
        lock (cached)
        {
            if (configure != null)
                cached.Module.Configure<AppFeature>(app => app.Configurator += configure);
            return cached.Module;
        }
    }

    private static CachedModule GetOrCreateModule(IServiceCollection services, ExplicitFeatureCatalog? catalog)
    {
        var entry = Modules.GetValue(services, key =>
            new CachedModule(catalog is null ? key.CreateModule() : key.CreateModule(catalog), catalog));
        if (!ReferenceEquals(entry.Catalog, catalog))
            throw new InvalidOperationException("The Elsa module for this service collection was created with a different feature provider. Use the same provider at every AddElsa and ConfigureElsa call.");
        return entry;
    }

    private sealed record CachedModule(IModule Module, ExplicitFeatureCatalog? Catalog);
}
