using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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

    private static readonly ConditionalWeakTable<FixedFeatureModule, Func<IServiceCollection, FixedFeatureModule>> FixedProviders = new();

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

    /// <summary>Applies the fixed plan selected by the generated host.</summary>
    public static IModule AddElsa(this IServiceCollection services, Action<IModule>? configure, Func<IServiceCollection, FixedFeatureModule> create)
    {
        var module = GetFixedModule(services, create);
        module.ConfigureHost(configure);
        module.Apply();
        return module;
    }

    /// <summary>Queues configuration for the fixed plan selected by the generated host.</summary>
    public static IModule ConfigureElsa(this IServiceCollection services, Action<IModule>? configure, Func<IServiceCollection, FixedFeatureModule> create)
    {
        var module = GetFixedModule(services, create);
        module.ConfigureHost(configure);
        return module;
    }

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
        var module = registry.GetOrAdd(services,
            static (key, provider) => provider is null ? key.CreateModule() : key.CreateModule(provider), catalog);
        if (module is not Module dynamicModule || !ReferenceEquals(dynamicModule.Catalog, catalog))
            throw DifferentProvider();
        return module;
    }

    private static FixedFeatureModule GetFixedModule(IServiceCollection services, Func<IServiceCollection, FixedFeatureModule> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        var registry = (ConcurrentDictionary<IServiceCollection, IModule>)Modules;
        var module = registry.GetOrAdd(services, static (collection, factory) =>
        {
            var module = factory(collection);
            if (module is null || !ReferenceEquals(collection, module.Services))
                throw new InvalidOperationException("The fixed feature factory must return a module for the requested service collection.");
            FixedProviders.GetValue(module, _ => factory);
            return module;
        }, create);
        if (module is not FixedFeatureModule fixedModule || !FixedProviders.TryGetValue(fixedModule, out var provider) || provider != create)
            throw DifferentProvider();
        return fixedModule;
    }

    private static InvalidOperationException DifferentProvider() =>
        new("The Elsa module for this service collection was created with a different feature provider. Use the same provider at every AddElsa and ConfigureElsa call.");
}
