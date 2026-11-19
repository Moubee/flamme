using Flamme.Common.Features;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Labels.Contracts;
using Flamme.Labels.Entities;
using Flamme.Labels.Services;
using Flamme.Workflows.Management;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Labels.Features;

/// <summary>
/// Enables functionality to tag workflows with labels.
/// </summary>
[DependsOn(typeof(MediatorFeature))]
public class LabelsFeature : FeatureBase
{
    /// <inheritdoc />
    public LabelsFeature(IModule module) : base(module)
    {
    }
    
    /// <summary>
    /// A delegate that provides an instance of an implementation of <see cref="ILabelStore"/>.
    /// </summary>
    public Func<IServiceProvider, ILabelStore> LabelStore { get; set; } = sp => sp.GetRequiredService<InMemoryLabelStore>();
    
    /// <summary>
    /// A delegate that provides an instance of an implementation of <see cref="IWorkflowDefinitionLabelStore"/>.
    /// </summary>
    public Func<IServiceProvider, IWorkflowDefinitionLabelStore> WorkflowDefinitionLabelStore { get; set; } = sp => sp.GetRequiredService<InMemoryWorkflowDefinitionLabelStore>();

    /// <inheritdoc />
    public override void Configure()
    {
        Module.AddFastEndpointsAssembly(GetType());
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services
            .AddMemoryStore<Label, InMemoryLabelStore>()
            .AddMemoryStore<WorkflowDefinitionLabel, InMemoryWorkflowDefinitionLabelStore>()
            .AddScoped(LabelStore)
            .AddScoped(WorkflowDefinitionLabelStore)
            .AddScoped<IWorkflowDefinitionFilterProvider, WorkflowDefinitionLabelFilterProvider>()
            ;

        Services.AddNotificationHandlersFrom(GetType());
    }
}