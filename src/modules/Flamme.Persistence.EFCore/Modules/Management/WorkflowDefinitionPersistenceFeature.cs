using Flamme.Expressions.Options;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Features;
using Flamme.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore.Modules.Management;

/// <summary>
/// Configures the <see cref="WorkflowDefinitionsFeature"/> feature with an Entity Framework Core persistence provider.
/// </summary>
[DependsOn(typeof(WorkflowManagementFeature))]
[DependsOn(typeof(WorkflowDefinitionsFeature))]
public class EFCoreWorkflowDefinitionPersistenceFeature(IModule module) : PersistenceFeatureBase<EFCoreWorkflowDefinitionPersistenceFeature, ManagementFlammeDbContext>(module)
{
    /// <inheritdoc />
    public override void Configure()
    {
        Module.Configure<WorkflowDefinitionsFeature>(feature =>
        {
            feature.WorkflowDefinitionStore = sp => sp.GetRequiredService<EFCoreWorkflowDefinitionStore>();
        });
    }

    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        AddEntityStore<WorkflowDefinition, EFCoreWorkflowDefinitionStore>();
    }
}
