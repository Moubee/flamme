using Elsa.Common.Services;
using Elsa.Features.Abstractions;
using Elsa.Features.Attributes;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Common.Features;

/// <summary>
/// Configures the system clock.
/// </summary>
[DependsOn(typeof(TimeProviderFeature))]
public class SystemClockFeature : FeatureBase
{
    /// <inheritdoc />
    public SystemClockFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.TryAddSingleton<ISystemClock>(sp => new SystemClock(sp.GetRequiredService<TimeProvider>()));
    }
}
