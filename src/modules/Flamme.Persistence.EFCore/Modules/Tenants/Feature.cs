using Flamme.Common.Multitenancy;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Tenants.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore.Modules.Tenants;

/// <summary>
/// Configures the <see cref="TenantManagementFeature"/> feature with an Entity Framework Core persistence provider.
/// </summary>
[DependsOn(typeof(TenantManagementFeature))]
public class EFCoreTenantManagementFeature(IModule module) : PersistenceFeatureBase<EFCoreTenantManagementFeature, TenantsFlammeDbContext>(module)
{
    /// <inheritdoc />
    public override void Configure()
    {
        Module.Configure<TenantManagementFeature>(feature =>
        {
            feature.WithTenantStore(sp => sp.GetRequiredService<EFCoreTenantStore>());
        });
    }

    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        AddEntityStore<Tenant, EFCoreTenantStore>();
    }

    protected override void ConfigureMigrations()
    {
        Module.ConfigureHostedService<RunMigrationsHostedService<TenantsFlammeDbContext>>(-100);
    }
}