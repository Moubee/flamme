using Flamme.Alterations.Core.Entities;
using Flamme.Alterations.Core.Models;
using Flamme.Workflows.Management.Models;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace Flamme.Persistence.EFCore.Modules.Alterations;

/// <summary>
/// DB context for the runtime module.
/// </summary>
[UsedImplicitly]
public class AlterationsFlammeDbContext : FlammeDbContextBase
{
    /// <inheritdoc />
    public AlterationsFlammeDbContext(DbContextOptions<AlterationsFlammeDbContext> options, IServiceProvider serviceProvider) : base(options, serviceProvider)
    {
    }

    /// <summary>
    /// The alteration plans.
    /// </summary>
    public DbSet<AlterationPlan> AlterationPlans { get; set; } = null!;

    /// <summary>
    /// The alteration jobs.
    /// </summary>
    public DbSet<AlterationJob> AlterationJobs { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<AlterationLogEntry>();
        modelBuilder.Ignore<AlterationWorkflowInstanceFilter>();
        modelBuilder.Ignore<TimestampFilter>();
        
        var configuration = new Configurations();
        modelBuilder.ApplyConfiguration<AlterationPlan>(configuration);
        modelBuilder.ApplyConfiguration<AlterationJob>(configuration);
        base.OnModelCreating(modelBuilder);
    }
}