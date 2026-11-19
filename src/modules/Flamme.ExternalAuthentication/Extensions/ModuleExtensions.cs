using Flamme.ExternalAuthentication.Features;
using Flamme.Features.Services;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class ModuleExtensions
{
    /// <summary>Enables the protocol-neutral External Authentication broker feature.</summary>
    public static IModule UseExternalAuthentication(this IModule module, Action<ExternalAuthenticationFeature>? configure = null) => module.Use(configure);
}
