using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore.Modules.Management;

/// <summary>
/// Configures the <see cref="WorkflowInstancesFeature"/> feature with an Entity Framework Core persistence provider.
/// </summary>
[DependsOn(typeof(WorkflowManagementFeature))]
[DependsOn(typeof(WorkflowInstancesFeature))]
public class EFCoreWorkflowInstancePersistenceFeature(IModule module) : PersistenceFeatureBase<EFCoreWorkflowInstancePersistenceFeature, ManagementFlammeDbContext>(module)
{
    /// <inheritdoc />
    public override void Configure()
    {
        Module.Configure<WorkflowInstancesFeature>(feature => feature.WorkflowInstanceStore = sp => sp.GetRequiredService<EFCoreWorkflowInstanceStore>());
    }

    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        AddEntityStore<WorkflowInstance, EFCoreWorkflowInstanceStore>();
    }
}