using Elsa.Features.Abstractions;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Common.Features;

/// <summary>
/// Configures the default time provider without replacing a host-supplied provider.
/// </summary>
public class TimeProviderFeature(IModule module) : FeatureBase(module)
{
    /// <inheritdoc />
    public override void Apply() => Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
}
