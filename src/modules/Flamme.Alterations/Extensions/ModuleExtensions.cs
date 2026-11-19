using Flamme.Alterations.Features;
using Flamme.Extensions;
using Flamme.Features.Services;

namespace Flamme.Alterations.Extensions;

/// <summary>
/// Adds the <see cref="AlterationsFeature"/>.
/// </summary>
public static class ModuleExtensions
{
    /// <summary>
    /// Adds the <see cref="AlterationsFeature"/>.
    /// </summary>
    public static IModule UseAlterations(this IModule module, Action<AlterationsFeature>? configure = null)
    {
        return module.Use(configure);
    }
}