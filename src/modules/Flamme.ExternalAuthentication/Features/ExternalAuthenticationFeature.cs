using Flamme.ExternalAuthentication.Options;
using Flamme.Common.Features;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.ExternalAuthentication.Features;

[DependsOn(typeof(TimeProviderFeature))]
public sealed class ExternalAuthenticationFeature(IModule module) : FeatureBase(module)
{
    /// <summary>Configures deployment-owned broker clients, connections, security boundaries, and operations.</summary>
    public Action<ExternalAuthenticationOptions>? ConfigureOptions { get; set; }

    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<ExternalAuthenticationFeature>();
    }

    public override void Apply()
    {
        Services.AddExternalAuthenticationServices(ConfigureOptions);
        Module.AddFastEndpointsFromModule();
    }
}
