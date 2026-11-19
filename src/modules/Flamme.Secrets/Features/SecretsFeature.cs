using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Secrets.Extensions;

namespace Flamme.Secrets.Features;

public class SecretsFeature(IModule module) : FeatureBase(module)
{
    public Action<SecretsOptions>? ConfigureOptions { get; set; }

    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<SecretsFeature>();
    }

    public override void Apply()
    {
        Services.AddSecretsServices(ConfigureOptions);
        Module.AddFastEndpointsFromModule();
    }
}
