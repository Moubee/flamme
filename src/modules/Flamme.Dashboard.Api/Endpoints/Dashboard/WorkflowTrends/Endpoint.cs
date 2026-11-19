using Flamme.Abstractions;
using Flamme.Dashboard.Abstractions.Contracts;
using Flamme.Dashboard.Abstractions.Models;
using Flamme.Dashboard.Api.Authorization;
using JetBrains.Annotations;

namespace Flamme.Dashboard.Api.Endpoints.Dashboard.WorkflowTrends;

[PublicAPI]
internal class Endpoint(IDashboardProvider dashboardProvider) : FlammeEndpoint<DashboardTrendRequest, DashboardTrendResponse>
{
    public override void Configure()
    {
        Post("/dashboard/workflow-trends");
        RequireAnyPermission(DashboardAccess.InstanceEndpointPermissions);
    }

    public override async Task<DashboardTrendResponse> ExecuteAsync(DashboardTrendRequest request, CancellationToken cancellationToken)
    {
        return await dashboardProvider.GetWorkflowTrendsAsync(request with { CanRead = DashboardAccess.CreateReadCheck(HttpContext) }, cancellationToken);
    }
}
