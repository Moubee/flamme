using System.Linq.Expressions;
using System.Text;
using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

// ReSharper disable once CheckNamespace
namespace Flamme.Persistence.EFCore.Extensions;

/// <summary>
/// Provides extension methods to perform bulk upsert operations for entities
/// in an Entity Framework Core context using PostgreSQL.
/// </summary>
public static class BulkUpsertExtensions
{
    internal const int DefaultBatchSize = 50;

    /// <summary>
    /// Performs a bulk upsert operation on a list of entities in the specified database context using a key selector.
    /// </summary>
    /// <typeparam name="TDbContext">The type of the database context.</typeparam>
    /// <typeparam name="TEntity">The type of the entity being upserted.</typeparam>
    /// <param name="dbContext">The database context where the bulk upsert operation will be executed.</param>
    /// <param name="entities">The list of entities to be upserted.</param>
    /// <param name="keySelector">An expression used to determine the key for upsert operations.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    public static async Task BulkUpsertAsync<TDbContext, TEntity>(
        this TDbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
        where TEntity : class, new()
    {
        await BulkUpsertAsync(dbContext, entities, keySelector, DefaultBatchSize, cancellationToken);
    }

    /// <summary>
    /// Performs a bulk upsert operation on a list of entities in the specified database context using a key selector and optional batch size.
    /// </summary>
    /// <typeparam name="TDbContext">The type of the database context.</typeparam>
    /// <typeparam name="TEntity">The type of the entity being upserted.</typeparam>
    /// <param name="dbContext">The database context where the bulk upsert operation will be executed.</param>
    /// <param name="entities">The list of entities to be upserted.</param>
    /// <param name="keySelector">An expression used to determine the key for upsert operations.</param>
    /// <param name="batchSize">The size of each batch for processing the upsert operation. Defaults to 50.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <exception cref="NotSupportedException">Thrown if the database provider for the context is not supported.</exception>
    public static async Task BulkUpsertAsync<TDbContext, TEntity>(
        this TDbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
        where TEntity : class, new()
    {
        await BulkUpsertCoreAsync(dbContext, entities, keySelector, batchSize, null, cancellationToken);
    }

    internal static Task BulkUpsertWithTenantOwnershipAsync<TDbContext, TEntity>(
        this TDbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector,
        string? writerTenantId,
        CancellationToken cancellationToken)
        where TDbContext : DbContext
        where TEntity : class, new()
    {
        return BulkUpsertCoreAsync(dbContext, entities, keySelector, DefaultBatchSize,
            typeof(Entity).IsAssignableFrom(typeof(TEntity)) ? writerTenantId : null, cancellationToken);
    }

    private static async Task BulkUpsertCoreAsync<TEntity>(
        DbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector,
        int batchSize,
        string? writerTenantId,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (entities.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var providerName = dbContext.Database.ProviderName ?? string.Empty;
        if (!PostgreSqlWriteKeys.Supports(dbContext))
            throw new NotSupportedException($"Provider '{providerName}' is not supported. Only PostgreSQL is supported.");

        var entityType = dbContext.Set<TEntity>().EntityType;
        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new NotSupportedException($"Bulk upserts require a table mapping for {typeof(TEntity).Name}.");
        var keyProperty = entityType.FindProperty(keySelector.GetMemberAccess().Name)
            ?? throw new ArgumentException("The key must select a mapped property.", nameof(keySelector));
        var getKey = keySelector.Compile();
        var keyConverter = keyProperty.GetTypeMapping().Converter;
        var providerKeys = new HashSet<object?>(keyProperty.GetProviderValueComparer());

        foreach (var entity in entities)
        {
            var key = getKey(entity);
            var providerKey = keyConverter == null ? key : keyConverter.ConvertToProvider(key);
            if (!providerKeys.Add(providerKey))
                throw new InvalidOperationException($"Cannot save {typeof(TEntity).Name}: duplicate key '{key}' in the batch.");
        }

        // Preserve caller order when mapped constraints or triggers can make DML order significant.
        var writeOrder = CanOrderByConflictKey(dbContext, table, keyProperty)
            ? entities.OrderBy(entity => PostgreSqlWriteKeys.GetProviderKey(keyProperty, getKey(entity)), StringComparer.Ordinal).ToArray()
            : entities;

        await PostgreSqlWriteKeys.ExecuteAsync(dbContext, async () =>
        {
            foreach (var batch in writeOrder.Chunk(batchSize))
            {
                var (sql, parameters) = GeneratePostgresUpsert(dbContext, batch, entityType, keyProperty, table, writerTenantId);
                var affected = await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
                if (affected != batch.Length)
                {
                    var reason = writerTenantId == null
                        ? "The database refused a batch row."
                        : "Check tenant ownership and the exact stored key. Shared rows need TenantId '*'.";
                    throw new InvalidOperationException(
                        $"Cannot save {typeof(TEntity).Name}: the database affected {affected} of {batch.Length} rows. {reason}");
                }
            }
        }, cancellationToken);
    }

    private static bool CanOrderByConflictKey(DbContext dbContext, StoreObjectIdentifier storeObject, IProperty keyProperty)
    {
        var table = dbContext.Model.GetRelationalModel().FindTable(storeObject.Name, storeObject.Schema);
        return table?.PrimaryKey is { Columns: [var column] } primaryKey &&
               column.Name == keyProperty.GetColumnName(storeObject) &&
               table.UniqueConstraints.All(constraint => constraint == primaryKey) &&
               !table.Indexes.Any(index => index.IsUnique) &&
               !table.ForeignKeyConstraints.Any(constraint => constraint.PrincipalTable == table) &&
               !table.Triggers.Any();
    }

    private static (string, object[]) GeneratePostgresUpsert<TEntity>(
        DbContext dbContext,
        IList<TEntity> entities,
        IEntityType entityType,
        IProperty keyProperty,
        StoreObjectIdentifier storeObject,
        string? writerTenantId)
        where TEntity : class
    {
        var sql = dbContext.GetService<ISqlGenerationHelper>();
        var props = entityType.GetProperties().ToList();

        var keyColumnName = sql.DelimitIdentifier(keyProperty.GetColumnName(storeObject)!);
        var columnNames = props
            .Select(p => sql.DelimitIdentifier(p.GetColumnName(storeObject)!))
            .ToList();

        var sb = new StringBuilder();
        var parameters = new List<object>();
        var parameterCount = 0;

        var table = sql.DelimitIdentifier(storeObject.Name, storeObject.Schema);
        sb.Append($"INSERT INTO {table} AS stored ({string.Join(", ", columnNames)}) VALUES ");

        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            var placeholders = new List<string>();

            foreach (var property in props)
            {
                var paramName = $"{{{parameterCount++}}}";

                var value = property.IsShadowProperty()
                    ? dbContext.Entry(entity).Property(property.Name).CurrentValue
                    : property.PropertyInfo?.GetValue(entity);

                var converter = property.GetTypeMapping().Converter;
                if (converter != null)
                    value = converter.ConvertToProvider(value);

                // Detect json/jsonb column types and cast the parameter so PostgreSQL accepts it.
                var columnType = property.GetColumnType();
                if (columnType.StartsWith("jsonb", StringComparison.OrdinalIgnoreCase))
                    placeholders.Add($"CAST({paramName} AS jsonb)");
                else if (columnType.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                    placeholders.Add($"CAST({paramName} AS json)");
                else
                    placeholders.Add(paramName);

                parameters.Add(value!);
            }

            sb.Append($"({string.Join(", ", placeholders)})");
            if (i < entities.Count - 1)
                sb.Append(", ");
        }

        sb.AppendLine();
        sb.AppendLine($"ON CONFLICT ({keyColumnName}) DO UPDATE SET");

        var updateAssignments = columnNames
            .Where(c => c != keyColumnName)
            .Select(c => $"{c} = EXCLUDED.{c}");

        sb.AppendLine(string.Join(", ", updateAssignments.DefaultIfEmpty($"{keyColumnName} = stored.{keyColumnName}")));

        if (writerTenantId != null)
        {
            var tenantProperty = entityType.FindProperty(nameof(Entity.TenantId))!;
            var tenantColumn = sql.DelimitIdentifier(tenantProperty.GetColumnName(storeObject)!);
            var tenantMapping = tenantProperty.GetRelationalTypeMapping();
            var defaultTenant = $"CAST({{{parameterCount}}} AS text)";
            var writerTenant = $"CAST({{{parameterCount + 1}}} AS text)";
            var sharedTenant = $"CAST({{{parameterCount + 2}}} AS text)";
            var storedTenant = $"COALESCE(stored.{tenantColumn}::text, {defaultTenant}) COLLATE pg_catalog.\"C\"";
            var incomingTenant = $"COALESCE(EXCLUDED.{tenantColumn}::text, {defaultTenant}) COLLATE pg_catalog.\"C\"";
            // Compare actual key spelling and owner even when the unique collation equates variants.
            sb.AppendLine($"WHERE stored.{keyColumnName}::text COLLATE pg_catalog.\"C\" = EXCLUDED.{keyColumnName}::text COLLATE pg_catalog.\"C\"");
            sb.AppendLine($"  AND {storedTenant} IS NOT DISTINCT FROM {incomingTenant}");
            sb.AppendLine($"  AND ({incomingTenant} IS NOT DISTINCT FROM {writerTenant} OR {incomingTenant} IS NOT DISTINCT FROM {sharedTenant})");
            using var command = dbContext.Database.GetDbConnection().CreateCommand();
            parameters.Add(tenantMapping.CreateParameter(command, "bulk_default_tenant", Tenant.DefaultTenantId));
            parameters.Add(tenantMapping.CreateParameter(command, "bulk_writer_tenant", writerTenantId));
            parameters.Add(tenantMapping.CreateParameter(command, "bulk_shared_tenant", Tenant.AgnosticTenantId));
        }

        return (sb.ToString(), parameters.ToArray());
    }
}
