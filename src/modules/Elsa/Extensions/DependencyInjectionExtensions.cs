using System.Collections.Concurrent;
using Elsa.Features;
using Elsa.Features.Implementations;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Extensions;

/// <summary>
/// Provides extension methods to <see cref="IServiceCollection"/>.
/// </summary>
public static class ModuleExtensions
{
    // The component host detaches its exact service collection/module pair when it stops.
    private static readonly IDictionary<IServiceCollection, IModule> Modules =
        new ConcurrentDictionary<IServiceCollection, IModule>(ReferenceEqualityComparer.Instance);

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

    private static IModule AddElsa(IModule module, Action<IModule>? configure)
    {
        module.Configure<AppFeature>(app => app.Configurator += configure);
        module.Apply();
        return module;
    }

    private static IModule ConfigureElsa(IModule module, Action<IModule>? configure)
    {
        if (configure != null)
            module.Configure<AppFeature>(app => app.Configurator += configure);
        return module;
    }

    private static IModule GetOrCreateModule(IServiceCollection services, ExplicitFeatureCatalog? catalog)
    {
        var registry = (ConcurrentDictionary<IServiceCollection, IModule>)Modules;
        var module = (Module)registry.GetOrAdd(services,
            static (key, provider) => provider is null ? key.CreateModule() : key.CreateModule(provider), catalog);
        if (!ReferenceEquals(module.Catalog, catalog))
            throw new InvalidOperationException("The Elsa module for this service collection was created with a different feature provider. Use the same provider at every AddElsa and ConfigureElsa call.");
        return module;
    }
}
