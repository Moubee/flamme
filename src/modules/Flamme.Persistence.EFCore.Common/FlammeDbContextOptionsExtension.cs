using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }
}