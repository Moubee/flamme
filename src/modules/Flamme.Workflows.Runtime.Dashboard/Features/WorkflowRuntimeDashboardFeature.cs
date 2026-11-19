using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Workflows.Runtime.Dashboard.Extensions;
using Flamme.Workflows.Runtime.Features;

namespace Flamme.Workflows.Runtime.Dashboard.Features;

[DependsOn(typeof(WorkflowRuntimeFeature))]
public class WorkflowRuntimeDashboardFeature(IModule module) : FeatureBase(module)
{
    public override void Apply()
    {
        Services.AddWorkflowRuntimeDashboard();
    }
}
