using Flamme.Labels.Features;

namespace Flamme.Persistence.EFCore.Modules.Labels;

public static class Extensions
{
    public static LabelsFeature UseEntityFrameworkCore(this LabelsFeature feature, Action<EFCoreLabelPersistenceFeature>? configure = null)
    {
        feature.Module.Configure(configure);
        return feature;
    }
}