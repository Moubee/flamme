using Flamme.AI.Host.Options;
using Flamme.AI.Host.Services;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.AI.Host.Features;

public class AIFeature(IModule module) : FeatureBase(module)
{
    public Action<AIHostOptions>? ConfigureOptions { get; set; }
    public Action<AIToolEnablementService>? ConfigureToolEnablement { get; set; }

    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<AIFeature>();
    }

    public override void Apply()
    {
        Services.AddAIHostServices(ConfigureOptions);
        if (ConfigureToolEnablement != null)
            Services.AddSingleton(ConfigureToolEnablement);
        Module.AddFastEndpointsFromModule();
    }

    public AIFeature EnableWorkflowProposalTools()
    {
        ConfigureToolEnablement += enablement =>
        {
            enablement.Enable("workflows.proposeCreate");
            enablement.Enable("workflows.proposeUpdate");
        };
        return this;
    }
}
