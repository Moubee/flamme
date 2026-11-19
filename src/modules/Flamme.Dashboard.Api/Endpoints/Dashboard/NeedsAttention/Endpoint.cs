using Flamme.Abstractions;
using Flamme.Dashboard.Abstractions.Contracts;
using Flamme.Dashboard.Abstractions.Models;
using Flamme.Dashboard.Api.Authorization;
using JetBrains.Annotations;

namespace Flamme.Dashboard.Api.Endpoints.Dashboard.NeedsAttention;

[PublicAPI]
internal class Endpoint(IDashboardProvider dashboardProvider) : FlammeEndpointWithoutRequest<DashboardNeedsAttentionResponse>
{
    public override void Configure()
    {
        Get("/dashboard/needs-attention");
        RequireAnyPermission(DashboardAccess.InstanceEndpointPermissions);
    }

    public override async Task<DashboardNeedsAttentionResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        var range = Query<string?>("range", false);
        var take = Query<int?>("take", false) ?? 8;
        var includeSystem = Query<bool>("includeSystem", false);
        return await dashboardProvider.GetNeedsAttentionAsync(new(range, includeSystem) { CanRead = DashboardAccess.CreateReadCheck(HttpContext) }, take, cancellationToken);
    }
}
