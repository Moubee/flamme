using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Elsa.Extensions;

/// <summary>
/// Adds extension methods to <see cref="IServiceCollection"/> that creates and configures modules.  
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Returns a new instance of an <see cref="IModule"/> implementation.
    /// </summary>
    public static IModule CreateModule(this IServiceCollection services) => new Module(services);

    /// <summary>
    /// Creates a module with explicit metadata and no managed discovery fallback.
    /// </summary>
    public static IModule CreateModule(this IServiceCollection services, ExplicitFeatureCatalog catalog,
        CompositionPolicy policy = CompositionPolicy.LegacyV1) => new Module(services, catalog, policy);

    /// <summary>
    /// Creates a managed-discovery module with an explicitly selected admission policy.
    /// </summary>
    public static IModule CreateModule(this IServiceCollection services, CompositionPolicy policy) => new Module(services, policy);

    /// <summary>
    /// Installs and configures the specified feature. If the feature was already installed, it is not added twice, which means it is safe to call this method multiple times.
    /// </summary>
    public static IModule Use<T>(this IModule module, Action<T>? configure = null) where T: class, IFeature
    {
        module.Configure(configure);
        return module;
    }
}