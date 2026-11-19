using Flamme.Features.Services;
using Flamme.Secrets.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class ModuleExtensions
{
    public static IModule UseSecrets(this IModule module, Action<SecretsFeature>? configure = null)
    {
        return module.Use(configure);
    }
}
