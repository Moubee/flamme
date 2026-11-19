using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Flamme.Persistence.EFCore;

/// <summary>
/// Represents handler for entities before saving changes.
/// </summary>
public interface IEntitySavingHandler
{
    /// <summary>
    /// Handles the entity before saving changes.
    /// </summary>
    ValueTask HandleAsync(FlammeDbContextBase dbContext, EntityEntry entry, CancellationToken cancellationToken = default);
}