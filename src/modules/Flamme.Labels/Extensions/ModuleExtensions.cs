using Flamme.Features.Services;
using Flamme.Labels.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class ModuleExtensions
{
    public static IModule UseLabels(this IModule configuration, Action<LabelsFeature>? configure = default)
    {
        configuration.Configure(configure);
        return configuration;
    }
}