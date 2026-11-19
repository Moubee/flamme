using Flamme.Common.Multitenancy;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Tenants.Features;

/// <summary>
/// Enables tenant management endpoints.
/// </summary>
public class TenantManagementFeature(IModule serviceConfiguration) : FeatureBase(serviceConfiguration)
{
    private Func<IServiceProvider, ITenantStore> _tenantStoreFactory = sp => sp.GetRequiredService<MemoryTenantStore>();

    public TenantManagementFeature WithTenantStore(Func<IServiceProvider, ITenantStore> factory)
    {
        _tenantStoreFactory = factory;
        return this;
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services
            .AddMemoryStore<Tenant, MemoryTenantStore>()
            .AddScoped(_tenantStoreFactory);
    }
}