using Flamme.Authorization;
using System.Net.Mime;
using System.Text.Json;
using Flamme.Abstractions;
using Flamme.Common.Multitenancy;
using Flamme.Common.Serialization;
using Flamme.Models;

namespace Flamme.Tenants.Endpoints.Tenants.List;

public class Endpoint(ITenantService tenantService) : FlammeEndpointWithoutRequest<ListResponse<Tenant>>
{
    public override void Configure()
    {
        Get("/tenants");
        RequirePermission(Flamme.Tenants.Permissions.TenantPermissions.Tenants, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tenants = await tenantService.ListAsync(ct);
        var response = new ListResponse<Tenant>(tenants.ToList());
        var json = JsonSerializer.Serialize(response, SerializerOptions.ConfigurationJsonSerializerOptions);
        await Send.StringAsync(json, contentType: MediaTypeNames.Application.Json, cancellation: ct);
    }
}