using Flamme.Persistence.EFCore;
using Flamme.Secrets.Models;
using Microsoft.EntityFrameworkCore;

namespace Flamme.Secrets.Persistence.EFCore;

/// <summary>
/// The database context for the Secrets module.
/// </summary>
public class SecretsFlammeDbContext : FlammeDbContextBase
{
    /// <inheritdoc />
    public SecretsFlammeDbContext(DbContextOptions<SecretsFlammeDbContext> options, IServiceProvider serviceProvider) : base(options, serviceProvider)
    {
    }

    /// <summary>
    /// The secrets.
    /// </summary>
    public DbSet<Secret> Secrets { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<SecretPayload>();
        modelBuilder.Ignore<SecretVersion>();
        modelBuilder.ApplyConfiguration(new SecretConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
