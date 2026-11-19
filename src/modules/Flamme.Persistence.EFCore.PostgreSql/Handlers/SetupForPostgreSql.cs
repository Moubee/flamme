using Flamme.Persistence.EFCore.Extensions;
using Flamme.Workflows.Management.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flamme.Persistence.EFCore.PostgreSql.Handlers;

/// <summary>
/// Represents a class that handles entity model creation for PostgreSQL databases.
/// </summary>
public class SetupForPostgreSql : IEntityModelCreatingHandler
{
    /// <inheritdoc />
    public void Handle(FlammeDbContextBase dbContext, ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        if (!dbContext.Database.IsPostgres())
            return;

        // Configure the WorkflowDefinition entity to use the PostgreSQL JSONB type for the StringData property:
        if (entityType.ClrType == typeof(WorkflowDefinition))
        {
            modelBuilder
                .Entity(entityType.Name)
                .Property("StringData")
                .HasColumnType("jsonb");
        }
    }
}