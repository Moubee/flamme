using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;

namespace Flamme.Features;

/// <summary>
/// A wrapper for invoking application-specific configuration, ensuring it is invoked lastly.
/// </summary>
[DependsOn(typeof(FlammeFeature))]
public class AppFeature(IModule module) : FeatureBase(module)
{
    /// <summary>
    /// The configurator to invoke.
    /// </summary>
    public Action<IModule>? Configurator { get; set; }

    /// <inheritdoc />
    public override void Configure()
    {
        Configurator?.Invoke(Module);
    }
}