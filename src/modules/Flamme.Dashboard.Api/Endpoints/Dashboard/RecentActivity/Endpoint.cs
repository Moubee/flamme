using Flamme.Abstractions;
using Flamme.Dashboard.Abstractions.Contracts;
using Flamme.Dashboard.Abstractions.Models;
using Flamme.Dashboard.Api.Authorization;
using JetBrains.Annotations;

namespace Flamme.Dashboard.Api.Endpoints.Dashboard.RecentActivity;

[PublicAPI]
internal class Endpoint(IDashboardProvider dashboardProvider) : FlammeEndpointWithoutRequest<DashboardRecentActivityResponse>
{
    public override void Configure()
    {
        Get("/dashboard/recent-activity");
        RequireAnyPermission(DashboardAccess.InstanceEndpointPermissions);
    }

    public override async Task<DashboardRecentActivityResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        var range = Query<string?>("range", false);
        var take = Query<int?>("take", false) ?? 20;
        var includeSystem = Query<bool>("includeSystem", false);
        return await dashboardProvider.GetRecentActivityAsync(new(range, includeSystem) { CanRead = DashboardAccess.CreateReadCheck(HttpContext) }, take, cancellationToken);
    }
}
