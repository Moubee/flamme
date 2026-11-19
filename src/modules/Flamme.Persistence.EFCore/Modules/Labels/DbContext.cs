using Flamme.Labels.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flamme.Persistence.EFCore.Modules.Labels;

public class LabelsFlammeDbContext : FlammeDbContextBase
{
    public LabelsFlammeDbContext(DbContextOptions<LabelsFlammeDbContext> options, IServiceProvider serviceProvider) : base(options, serviceProvider)
    {
    }

    public DbSet<Label> Labels { get; set; } = null!;
    public DbSet<WorkflowDefinitionLabel> WorkflowDefinitionLabels { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        var config = new Configurations();
        modelBuilder.ApplyConfiguration<Label>(config);
        modelBuilder.ApplyConfiguration<WorkflowDefinitionLabel>(config);
    }
}