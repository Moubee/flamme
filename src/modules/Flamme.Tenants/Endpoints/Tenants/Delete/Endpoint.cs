using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Multitenancy;

namespace Flamme.Tenants.Endpoints.Tenants.Delete;

public class Endpoint(ITenantService tenantService, ITenantStore store) : FlammeEndpointWithoutRequest<Tenant>
{
    public override void Configure()
    {
        Delete("/tenants/{id}");
        RequirePermission(Flamme.Tenants.Permissions.TenantPermissions.Tenants, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<string>("id")!;
        var found = await store.DeleteAsync(id, ct);
        
        if (!found)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        
        await tenantService.RefreshAsync(ct);
        await Send.OkAsync(cancellation: ct);
    }
}