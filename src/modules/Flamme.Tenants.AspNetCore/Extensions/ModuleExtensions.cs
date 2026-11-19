using Flamme.Features.Services;
using Flamme.Tenants.AspNetCore.Features;
using Flamme.Tenants.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Tenants.Extensions;

/// <summary>
/// Extensions for <see cref="IModule"/> that installs the <see cref="TenantsFeature"/> feature.
/// </summary>
public static class ModuleExtensions
{
    /// <summary>
    /// Installs and configures the <see cref="TenantsFeature"/> feature.
    /// </summary>
    public static IModule UseTenantHttpRouting(this IModule module, Action<MultitenantHttpRoutingFeature>? configure = null)
    {
        module.Configure(configure);
        return module;
    }
}