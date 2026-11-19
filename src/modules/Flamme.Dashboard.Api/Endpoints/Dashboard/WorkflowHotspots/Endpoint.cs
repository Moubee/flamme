using Flamme.Abstractions;
using Flamme.Dashboard.Abstractions.Contracts;
using Flamme.Dashboard.Abstractions.Models;
using Flamme.Dashboard.Api.Authorization;
using JetBrains.Annotations;

namespace Flamme.Dashboard.Api.Endpoints.Dashboard.WorkflowHotspots;

[PublicAPI]
internal class Endpoint(IDashboardProvider dashboardProvider) : FlammeEndpoint<DashboardWorkflowHotspotsRequest, DashboardWorkflowHotspotsResponse>
{
    public override void Configure()
    {
        Post("/dashboard/workflow-hotspots");
        RequireAnyPermission(DashboardAccess.InstanceEndpointPermissions);
    }

    public override async Task<DashboardWorkflowHotspotsResponse> ExecuteAsync(DashboardWorkflowHotspotsRequest request, CancellationToken cancellationToken)
    {
        return await dashboardProvider.GetWorkflowHotspotsAsync(request with { CanRead = DashboardAccess.CreateReadCheck(HttpContext) }, cancellationToken);
    }
}
