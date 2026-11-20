using System.Linq.Expressions;
using System.Text;
using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        await PostgreSqlWriteKeys.ExecuteAsync(dbContext, async () =>
        {
            var writeOrder = entities.Count > 1 && await CanOrderByConflictKeyAsync(dbContext, table, keyProperty, cancellationToken)
                ? entities.OrderBy(entity => PostgreSqlWriteKeys.GetProviderKey(keyProperty, getKey(entity)), StringComparer.Ordinal).ToArray()
                : entities;
            var storedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var batch in writeOrder.Chunk(batchSize))
            {
                var (sql, parameters) = GeneratePostgresUpsert(dbContext, batch, entityType, keyProperty, table, writerTenantId);
                var affected = await ReadReturnedKeysAsync(dbContext, sql, parameters, cancellationToken);
                if (affected.Count != batch.Length)
                {
                    var reason = writerTenantId == null
                        ? "The database refused a batch row."
                        : "Check tenant ownership and the exact stored key. Shared rows need TenantId '*'.";
                    throw new InvalidOperationException(
                        $"Cannot save {typeof(TEntity).Name}: the database affected {affected.Count} of {batch.Length} rows. {reason}");
                }

                // The conflict target can equate keys that are distinct to EF (for example, citext aliases).
                foreach (var key in affected)
                {
                    if (key != null && !storedKeys.Add(key))
                        throw new InvalidOperationException($"Cannot save {typeof(TEntity).Name}: duplicate stored key '{key}' in the batch.");
                }
            }
        }, cancellationToken);
    }

    private static async Task<List<string?>> ReadReturnedKeysAsync(
        DbContext dbContext, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        // Execute DML directly so the query pipeline cannot retry it or wrap its PostgreSQL error.
        var command = dbContext.GetService<IRawSqlCommandBuilder>().Build(sql, parameters, dbContext.Model);
        var arguments = CreateCommandParameters(dbContext, command);
        await using var reader = await command.RelationalCommand.ExecuteReaderAsync(arguments, cancellationToken);
        var keys = new List<string?>();
        while (await reader.ReadAsync(cancellationToken))
            keys.Add(reader.DbDataReader.IsDBNull(0) ? null : reader.DbDataReader.GetString(0));
        return keys;
    }

    private static async Task<bool> CanOrderByConflictKeyAsync(
        DbContext dbContext, StoreObjectIdentifier storeObject, IProperty keyProperty, CancellationToken cancellationToken)
    {
        var sql = dbContext.GetService<ISqlGenerationHelper>();
        var tableName = sql.DelimitIdentifier(storeObject.Name, storeObject.Schema);
        var keyColumn = keyProperty.GetColumnName(storeObject)!;
        var database = dbContext.Database;
        // Keep the catalog read in the write's transaction without invoking the EF query strategy.
        var eligibility = dbContext.GetService<IRawSqlCommandBuilder>().Build("""
            SELECT pg_catalog.current_setting('transaction_isolation') IN ('read committed', 'read uncommitted')
              AND EXISTS (
                SELECT 1
                FROM pg_catalog.pg_class AS target
                JOIN pg_catalog.pg_index AS pk ON pk.indrelid = target.oid AND pk.indisprimary
                JOIN pg_catalog.pg_attribute AS key_column ON key_column.attrelid = target.oid AND key_column.attnum = pk.indkey[0]
                JOIN pg_catalog.pg_opclass AS opclass ON opclass.oid = pk.indclass[0]
                JOIN pg_catalog.pg_collation AS key_collation ON key_collation.oid = pk.indcollation[0]
                WHERE target.oid = pg_catalog.to_regclass({0})
                  AND target.relkind = 'r' AND NOT target.relispartition AND NOT target.relhassubclass
                  AND NOT target.relhastriggers AND NOT target.relhasrules
                  AND pk.indnkeyatts = 1 AND pk.indisvalid AND key_column.attname = {1}
                  AND key_column.atttypid IN ('pg_catalog.text'::regtype, 'pg_catalog.varchar'::regtype)
                  AND opclass.opcdefault AND opclass.opcnamespace = 'pg_catalog'::regnamespace
                  AND key_collation.collisdeterministic
                  AND (pg_catalog.has_table_privilege(target.oid, 'INSERT')
                       OR pg_catalog.has_table_privilege(target.oid, 'UPDATE'))
                  AND NOT EXISTS (
                      SELECT 1 FROM pg_catalog.pg_index AS other
                      WHERE other.indrelid = target.oid AND other.indexrelid <> pk.indexrelid
                        AND (other.indisunique OR other.indisexclusion)
                  )
              )
            """, [tableName, keyColumn], dbContext.Model);
        var command = eligibility.RelationalCommand;
        var arguments = CreateCommandParameters(dbContext, eligibility);
        if (await command.ExecuteScalarAsync(arguments, cancellationToken) is not true)
            return false;

        // The upsert takes this writer-compatible lock anyway. Take it before rechecking the catalogs
        // so that concurrent DDL cannot make a newly discovered write order unsafe.
        var lockTable = $"LOCK TABLE ONLY {tableName} IN ROW EXCLUSIVE MODE";
        await database.ExecuteSqlRawAsync(lockTable, cancellationToken);
        return await command.ExecuteScalarAsync(arguments, cancellationToken) is true;
    }

    private static RelationalCommandParameterObject CreateCommandParameters(DbContext dbContext, RawSqlCommand command) =>
        new(dbContext.GetService<IRelationalConnection>(), command.ParameterValues, null, dbContext,
            dbContext.GetService<IRelationalCommandDiagnosticsLogger>(), CommandSource.ExecuteSqlRaw);

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

        sb.AppendLine($"RETURNING stored.{keyColumnName}::text AS \"Value\"");
        return (sb.ToString(), parameters.ToArray());
    }
}
