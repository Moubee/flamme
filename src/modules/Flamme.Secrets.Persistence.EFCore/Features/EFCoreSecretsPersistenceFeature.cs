using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Persistence.EFCore;
using Flamme.Secrets.Contracts;
using Flamme.Secrets.Features;
using Flamme.Secrets.Models;
using Flamme.Secrets.Persistence.EFCore.Repositories;
using Flamme.Secrets.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Secrets.Persistence.EFCore.Features;

/// <summary>
/// Configures the <see cref="SecretsFeature"/> feature with Entity Framework Core persistence.
/// </summary>
[DependsOn(typeof(SecretsFeature))]
public class EFCoreSecretsPersistenceFeature(IModule module) : PersistenceFeatureBase<EFCoreSecretsPersistenceFeature, SecretsFlammeDbContext>(module)
{
    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        AddStore<Secret, EFCoreSecretRepository>();
        Services.AddScoped<ISecretRepository, EFCoreSecretRepository>();
        Services.AddScoped<ISecretManager, DefaultSecretManager>();
        Services.AddScoped<ISecretResolver, DefaultSecretResolver>();
    }
}
