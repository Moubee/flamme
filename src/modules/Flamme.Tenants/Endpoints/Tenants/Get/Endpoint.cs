using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Multitenancy;
using Flamme.Models;

namespace Flamme.Tenants.Endpoints.Tenants.Get;

public class Endpoint(ITenantService tenantService) : FlammeEndpointWithoutRequest<Tenant>
{
    public override void Configure()
    {
        Get("/tenants/{id}");
        RequirePermission(Flamme.Tenants.Permissions.TenantPermissions.Tenants, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<string>("id")!;
        var tenant = await tenantService.FindAsync(id, ct);
        
        if (tenant == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        
        await Send.OkAsync(tenant, ct);
    }
}