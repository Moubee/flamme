using Flamme.Dashboard.Abstractions.Extensions;
using Flamme.Workflows.Runtime.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Runtime.Dashboard.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowRuntimeDashboard(this IServiceCollection services)
    {
        return services.AddDashboardContributor<WorkflowDashboardContributor>();
    }
}
