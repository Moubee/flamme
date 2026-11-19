using Flamme.Features.Services;
using Flamme.Hosting.Management.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class ModuleExtensions
{
    public static IModule UseApplicationCluster(this IModule module, Action<ClusteringFeature>? configure = default)
    {
        module.Configure<ClusteringFeature>(management =>
        {
            configure?.Invoke(management);
        });
        return module;
    }
}