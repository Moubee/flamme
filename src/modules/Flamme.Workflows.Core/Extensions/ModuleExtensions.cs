using Flamme.Features.Services;
using Flamme.Workflows.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class ModuleExtensions
{
    extension(IModule configuration)
    {
        public IModule UseWorkflows(Action<WorkflowsFeature>? configure = null)
        {
            configuration.Configure(configure);
            return configuration;
        }

        public IModule UseFlowchart(Action<FlowchartFeature>? configure = null)
        {
            configuration.Configure(configure);
            return configuration;
        }
    }
}