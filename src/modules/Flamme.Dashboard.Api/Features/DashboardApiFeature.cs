using Flamme.Dashboard.Api.Extensions;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;

namespace Flamme.Dashboard.Api.Features;

public class DashboardApiFeature(IModule module) : FeatureBase(module)
{
    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<DashboardApiFeature>();
    }

    public override void Apply()
    {
        Services.AddDashboardApiServices();
        Module.AddFastEndpointsFromModule();
    }
}
