using Flamme.ExternalAuthentication.OpenIdConnect.Features;
using Flamme.Features.Services;

namespace Flamme.Extensions;

public static class ModuleExtensions
{
    public static IModule UseOpenIdConnectExternalAuthentication(
        this IModule module,
        Action<OpenIdConnectExternalAuthenticationFeature>? configure = null)
    {
        return module.Use(configure);
    }
}
