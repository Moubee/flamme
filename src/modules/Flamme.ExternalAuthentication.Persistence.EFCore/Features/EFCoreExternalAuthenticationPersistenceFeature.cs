using Flamme.ExternalAuthentication.Features;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Persistence.EFCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.ExternalAuthentication.Persistence.EFCore.Features;

/// <summary>
/// Configures the <see cref="ExternalAuthenticationFeature"/> feature with Entity Framework Core persistence.
/// </summary>
[DependsOn(typeof(ExternalAuthenticationFeature))]
public class EFCoreExternalAuthenticationPersistenceFeature(IModule module) : PersistenceFeatureBase<EFCoreExternalAuthenticationPersistenceFeature, ExternalAuthenticationFlammeDbContext>(module)
{
    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        Services.AddExternalAuthenticationEntityFrameworkCore();
    }
}
