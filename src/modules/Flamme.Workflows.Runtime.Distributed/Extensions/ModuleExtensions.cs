using Flamme.Workflows.Runtime.Distributed.Features;
using Flamme.Workflows.Runtime.Features;

namespace Flamme.Workflows.Runtime.Distributed.Extensions;

public static class ModuleExtensions
{
    public static WorkflowRuntimeFeature UseDistributedRuntime(this WorkflowRuntimeFeature feature, Action<DistributedRuntimeFeature>? configure = null)
    {
        feature.Module.Configure(configure);
        return feature;
    }
}