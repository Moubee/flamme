using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Multitenancy;

namespace Flamme.Tenants.Endpoints.Tenants.Refresh;

public class Endpoint(ITenantService tenantService) : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/tenants/refresh");
        RequirePermission(Flamme.Tenants.Permissions.TenantPermissions.Tenants, "refresh");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await tenantService.RefreshAsync(ct);
        await Send.OkAsync(cancellation: ct);
    }
}