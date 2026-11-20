using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flamme.Persistence.EFCore;

/// <summary>
/// Provides options for configuring Flamme's Entity Framework Core integration.
/// </summary>
public class FlammeDbContextOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FlammeDbContextOptionsExtension"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public FlammeDbContextOptionsExtension(FlammeDbContextOptions? options)
    {
        Options = options;
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public FlammeDbContextOptions? Options { get; }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => new CustomDbContextOptionsExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        // EF applies the caller's explicit ReplaceService overrides after extension defaults.
        services.Replace(ServiceDescriptor.Singleton<IModelCacheKeyFactory, FlammeModelCacheKeyFactory>());
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }
}