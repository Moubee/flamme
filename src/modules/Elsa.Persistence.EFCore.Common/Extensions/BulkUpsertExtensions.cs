using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

// ReSharper disable once CheckNamespace
namespace Elsa.Persistence.EFCore.Extensions;

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
        if (entities.Count == 0)
            return;

        var providerName = dbContext.Database.ProviderName ?? string.Empty;
        if (!providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Provider '{providerName}' is not supported. Only PostgreSQL is supported.");

        // Loop through batched entities
        foreach (var batch in entities.Chunk(batchSize))
        {
            // Generate SQL and parameters
            var (sql, parameters) = GeneratePostgresUpsert(dbContext, batch, keySelector);

            await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }
    }

    private static (string, object[]) GeneratePostgresUpsert<TEntity>(
        DbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector)
        where TEntity : class
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity))!;
        var tableName = entityType.GetTableName();
        var storeObject = StoreObjectIdentifier.Table(tableName!, entityType.GetSchema());

        var props = entityType.GetProperties().ToList();

        var keyProp = entityType.FindProperty(keySelector.GetMemberAccess().Name)!;
        var keyColumnName = keyProp.GetColumnName(storeObject);
        var columnNames = props
            .Select(p => p.GetColumnName(storeObject)!)
            .ToList();

        var sb = new StringBuilder();
        var parameters = new List<object>();
        var parameterCount = 0;

        sb.Append($"INSERT INTO \"{storeObject.Schema}\".\"{storeObject.Name}\" ({string.Join(", ", columnNames.Select(c => $"\"{c}\""))}) VALUES ");

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
        sb.AppendLine($"ON CONFLICT (\"{keyColumnName}\") DO UPDATE SET");

        var updateAssignments = columnNames
            .Where(c => c != keyColumnName)
            .Select(c => $"\"{c}\" = EXCLUDED.\"{c}\"");

        sb.AppendLine(string.Join(", ", updateAssignments) + ";");

        return (sb.ToString(), parameters.ToArray());
    }
}
