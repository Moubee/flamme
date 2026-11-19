using Flamme.Features.Services;
using Flamme.Expressions.Liquid.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class ModuleExtensions
{
    /// <summary>
    /// Setup the <see cref="LiquidFeature"/> feature.
    /// </summary>
    public static IModule UseLiquid(this IModule module, Action<LiquidFeature>? configure = default)
    {
        module.Configure(configure);
        return module;
    }
}