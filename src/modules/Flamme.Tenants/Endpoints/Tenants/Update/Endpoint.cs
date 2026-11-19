using Flamme.Authorization;
using System.Text.Json;
using Flamme.Abstractions;
using Flamme.Common.Multitenancy;
using Flamme.Common.Serialization;

namespace Flamme.Tenants.Endpoints.Tenants.Update;

public class Endpoint(ITenantService tenantService, ITenantStore tenantStore) : FlammeEndpoint<UpdatedTenant, Tenant>
{
    public override void Configure()
    {
        Post("/tenants/{id}");
        RequirePermission(Flamme.Tenants.Permissions.TenantPermissions.Tenants, CoreVerbs.Write);
    }

    public override async Task HandleAsync(UpdatedTenant req, CancellationToken ct)
    {
        var id = Route<string>("id")!;
        if (id == "-") id = string.Empty;
        var tenant = await tenantStore.FindAsync(id, ct);
        
        if (tenant == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        
        tenant.TenantId = req.TenantId;
        tenant.Name = req.Name.Trim();
        tenant.Configuration = Serializers.DeserializeConfiguration(req.Configuration);
        await tenantStore.UpdateAsync(tenant, ct);
        await tenantService.RefreshAsync(ct);
        await Send.OkAsync(tenant, ct);
    }
}

public class UpdatedTenant
{
    public string Id { get; set; } = default!;
    public string? TenantId { get; set; }
    public string Name { get; set; } = default!;
    public JsonElement? Configuration { get; set; }
}