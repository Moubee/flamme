using Flamme.Alterations.Core.Entities;
using Flamme.Alterations.Features;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore.Modules.Alterations;

/// <summary>
/// Configures the default workflow runtime to use EF Core persistence providers.
/// </summary>
[DependsOn(typeof(AlterationsFeature))]
public class EFCoreAlterationsPersistenceFeature(IModule module) : PersistenceFeatureBase<EFCoreAlterationsPersistenceFeature, AlterationsFlammeDbContext>(module)
{
    /// <inheritdoc />
    public override void Configure()
    {
        Module.Configure<AlterationsFeature>(feature =>
        {
            feature.AlterationPlanStoreFactory = sp => sp.GetRequiredService<EFCoreAlterationPlanStore>();
            feature.AlterationJobStoreFactory = sp => sp.GetRequiredService<EFCoreAlterationJobStore>();
        });
    }

    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        AddEntityStore<AlterationPlan, EFCoreAlterationPlanStore>();
        AddEntityStore<AlterationJob, EFCoreAlterationJobStore>();
    }
}