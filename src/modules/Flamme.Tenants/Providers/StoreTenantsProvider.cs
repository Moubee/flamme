using Flamme.Common.Multitenancy;
using JetBrains.Annotations;

namespace Flamme.Tenants.Providers;

[UsedImplicitly]
public class StoreTenantsProvider(ITenantStore store) : ITenantsProvider
{
    public async Task<IEnumerable<Tenant>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await store.ListAsync(cancellationToken);
    }

    public Task<Tenant?> FindAsync(TenantFilter filter, CancellationToken cancellationToken = default)
    {
        return store.FindAsync(filter, cancellationToken);
    }
}