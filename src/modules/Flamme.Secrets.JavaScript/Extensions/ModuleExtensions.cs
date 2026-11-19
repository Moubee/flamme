using Flamme.Features.Services;
using Flamme.Secrets.JavaScript.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class ModuleExtensions
{
    public static IModule UseSecretsJavaScript(this IModule module, Action<SecretsJavaScriptFeature>? configure = null)
    {
        return module.Use(configure);
    }
}
