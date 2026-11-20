using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flamme.Persistence.EFCore;

/// <summary>
/// Coordinates check-then-write operations for a mapped PostgreSQL key across connections.
/// </summary>
internal static class PostgreSqlWriteKeys
{
    internal static bool Supports(DbContext context) =>
        context.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static Transaction? GetEnlistedOrAmbientTransaction(DbContext context) =>
        context.Database.GetEnlistedTransaction() ??
        (context.GetService<IDbContextTransactionManager>() as ITransactionEnlistmentManager)?.CurrentAmbientTransaction;

    internal static Task ExecuteAsync(DbContext context, Func<Task> write, CancellationToken cancellationToken) =>
        ExecuteAsync(context, static () => Task.FromResult(false), _ => write(), cancellationToken);

    internal static async Task ExecuteAsync<TPreparation>(
        DbContext context,
        Func<Task<TPreparation>> prepare,
        Func<TPreparation, Task> write,
        CancellationToken cancellationToken)
    {
        var database = context.Database;
        if (database.CurrentTransaction is not null)
        {
            await ExecuteAtSavepointAsync();
            return;
        }

        var enlistedOrAmbient = GetEnlistedOrAmbientTransaction(context);
        if (enlistedOrAmbient is not null)
        {
            // Keep the lock and subsequent EF commands on the same enlisted connection.
            await database.OpenConnectionAsync(cancellationToken);
            try
            {
                if (database.GetEnlistedTransaction() is null)
                    database.EnlistTransaction(enlistedOrAmbient);

                await ExecuteAtSavepointAsync();
            }
            finally
            {
                await database.CloseConnectionAsync();
            }
            return;
        }

        var strategy = database.CreateExecutionStrategy();
        var failure = await strategy.ExecuteAsync<ExceptionDispatchInfo?>(async ct =>
        {
            var writing = false;
            try
            {
                // A subsequent read must see the write that released a contended key.
                await using var transaction = await database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
                var prepared = await prepare();
                writing = true;
                await write(prepared);
                await transaction.CommitAsync(ct);
                return null;
            }
            catch (Exception exception) when (writing)
            {
                // Pass write and commit failures through without letting the strategy replay them.
                return ExceptionDispatchInfo.Capture(exception);
            }
        }, cancellationToken);
        failure?.Throw();

        async Task ExecuteAtSavepointAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep savepoint commands independent of cancellation of the protected work.
            const string savepoint = "flamme_store_write";
            await database.ExecuteSqlRawAsync("SAVEPOINT " + savepoint, CancellationToken.None);
            try
            {
                await write(await prepare());
                await database.ExecuteSqlRawAsync("RELEASE SAVEPOINT " + savepoint, CancellationToken.None);
            }
            catch (Exception writeException)
            {
                try
                {
                    await database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT " + savepoint, CancellationToken.None);
                    await database.ExecuteSqlRawAsync("RELEASE SAVEPOINT " + savepoint, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException("Store write failed and restoring its savepoint failed.", writeException, rollbackException);
                }

                throw;
            }
        }
    }

    internal static async Task AcquireAsync(
        DbContext context,
        IProperty keyProperty,
        StoreObjectIdentifier table,
        string? key,
        CancellationToken cancellationToken)
    {
        if (key == null)
            return;

        var database = context.Database;
        if (database.CurrentTransaction is null && database.GetEnlistedTransaction() is null)
            throw new InvalidOperationException("PostgreSQL write keys require an active database transaction.");

        var sql = context.GetService<ISqlGenerationHelper>();
        var tableName = sql.DelimitIdentifier(table.Name, table.Schema);
        var column = keyProperty.GetColumnName(table)!;
        var keyColumn = sql.DelimitIdentifier(column);
        using var command = database.GetDbConnection().CreateCommand();
        var keyParameter = keyProperty.GetRelationalTypeMapping().CreateParameter(command, "flamme_key", key);

        // A zero-row subquery supplies the physical key's type and collation.
        // Populate only that field so unrelated domain constraints stay out of the lock query.
        // The relation OID keeps qualified and unqualified mappings in the same lock namespace.
#pragma warning disable EF1002 // Only provider-delimited identifiers are interpolated. All values are bound parameters.
        await database.ExecuteSqlRawAsync($$"""
            SELECT pg_catalog.pg_advisory_xact_lock(
                pg_catalog.hash_record_extended(
                    pg_catalog.jsonb_populate_record(
                        (SELECT typed_key
                         FROM (
                             SELECT (SELECT {{EscapeFormat(keyColumn)}} FROM {{EscapeFormat(tableName)}} LIMIT 0) AS k
                         ) AS typed_key),
                        pg_catalog.jsonb_build_object('k', {2})),
                    pg_catalog.hashtextextended(
                        pg_catalog.concat('flamme:store:', pg_catalog.to_regclass({0})::oid, ':', {1}), 0)))
            """, [tableName, column, keyParameter], cancellationToken);
#pragma warning restore EF1002

        // ExecuteSqlRaw formats parameter placeholders after identifiers are delimited.
        static string EscapeFormat(string identifier) =>
            identifier.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
    }

    internal static string GetProviderKey(IProperty property, string? key)
    {
        var converter = property.GetTypeMapping().Converter;
        return (converter == null ? key : converter.ConvertToProvider(key)) switch
        {
            string text => text,
            byte[] bytes => Convert.ToHexString(bytes),
            decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
            DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
