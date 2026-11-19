using Flamme.Extensions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Labels.Entities;
using Flamme.Labels.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore.Modules.Labels;

/// <summary>
/// Configures the <see cref="LabelsFeature"/> feature with an Entity Framework Core persistence provider.
/// </summary>
[DependsOn(typeof(LabelsFeature))]
public class EFCoreLabelPersistenceFeature(IModule module) : PersistenceFeatureBase<EFCoreLabelPersistenceFeature, LabelsFlammeDbContext>(module)
{
    public override void Configure()
    {
        Module.UseLabels(labels =>
        {
            labels.LabelStore = sp => sp.GetRequiredService<EFCoreLabelStore>();
            labels.WorkflowDefinitionLabelStore = sp => sp.GetRequiredService<EFCoreWorkflowDefinitionLabelStore>();
        });
    }

    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        AddEntityStore<Label, EFCoreLabelStore>();
        AddEntityStore<WorkflowDefinitionLabel, EFCoreWorkflowDefinitionLabelStore>();
    }
}