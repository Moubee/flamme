using Flamme.Extensions;
using Flamme.Features.Services;
using Flamme.Resilience.Features;

namespace Flamme.Resilience.Extensions;

public static class ModuleExtensions
{
    public static IModule UseResilience(this IModule module, Action<ResilienceFeature>? configure = null)
    {
        return module.Use(configure);
    }
}