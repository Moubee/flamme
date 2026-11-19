using Flamme.ExternalAuthentication.Features;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.ExternalAuthentication.OpenIdConnect.Features;

[DependsOn(typeof(ExternalAuthenticationFeature))]
public sealed class OpenIdConnectExternalAuthenticationFeature(IModule module) : FeatureBase(module)
{
    public override void Apply()
    {
        Services.AddOpenIdConnectExternalAuthentication();
    }
}
