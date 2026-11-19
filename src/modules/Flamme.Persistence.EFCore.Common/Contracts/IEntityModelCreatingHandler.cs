using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flamme.Persistence.EFCore;

/// <summary>
/// Represents handler for entity model creation.
/// </summary>
public interface IEntityModelCreatingHandler
{
    /// <summary>
    /// Handles the entity model being created.
    /// </summary>
    void Handle(FlammeDbContextBase dbContext, ModelBuilder modelBuilder, IMutableEntityType entityType);
}