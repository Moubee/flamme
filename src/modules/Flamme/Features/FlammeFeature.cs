using Flamme.Common.Features;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Features;
using Flamme.Workflows.Management.Features;
using Flamme.Workflows.Runtime.Features;

namespace Flamme.Features;

/// <summary>
/// Represents Flamme as a feature of the system.
/// </summary>
[DependsOn(typeof(MediatorFeature))]
[DependsOn(typeof(WorkflowsFeature))]
[DependsOn(typeof(FlowchartFeature))]
[DependsOn(typeof(DefaultWorkflowRuntimeFeature))]
[DependsOn(typeof(WorkflowManagementFeature))]
public class FlammeFeature : FeatureBase
{
    /// <summary>
    /// Set this to true to opt out of automatically registering activities from Flamme.Workflows.Core.
    /// </summary>
    public bool DisableAutomaticActivityRegistration { get; set; }

    /// <inheritdoc />
    public FlammeFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Module
            .UseWorkflows(workflows => workflows
                .WithDefaultWorkflowExecutionPipeline()
                .WithDefaultActivityExecutionPipeline())
            .UseWorkflowManagement(management =>
            {
                if (!DisableAutomaticActivityRegistration)
                    management
                        .AddActivitiesFrom<WriteLine>()
                        .RemoveActivity<ReadLine>() // ReadLine is not commonly used and can cause "hanging" containers when awaiting user input. Better to opt-in explicitly. 
                        ;
            });
    }
}