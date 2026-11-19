using Flamme.Dashboard.Api.Features;
using Flamme.Features.Services;

namespace Flamme.Extensions;

public static class DashboardModuleExtensions
{
    public static IModule UseDashboardApi(this IModule module, Action<DashboardApiFeature>? configure = null)
    {
        module.Configure(configure);
        return module;
    }
}
