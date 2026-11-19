using Flamme.Features.Services;
using Flamme.Workflows.Runtime.Dashboard.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class WorkflowRuntimeDashboardModuleExtensions
{
    public static IModule UseWorkflowRuntimeDashboard(this IModule module, Action<WorkflowRuntimeDashboardFeature>? configure = null)
    {
        module.Configure(configure);
        return module;
    }
}
