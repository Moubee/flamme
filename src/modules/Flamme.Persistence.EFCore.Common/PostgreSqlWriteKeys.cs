using System.Data;
using System.Data.Common;
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
    private const string StoreSavepoint = "flamme_store_write";

    internal static bool Supports(DbContext context) =>
        context.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static Transaction? GetAmbientTransaction(DbContext context) =>
        (context.GetService<IDbContextTransactionManager>() as ITransactionEnlistmentManager)?.CurrentAmbientTransaction;

    private static bool HasAmbientEnlistmentOptOut(DatabaseFacade database)
    {
        var connectionString = database.GetConnectionString();
        if (connectionString is null || !connectionString.Contains("Enlist", StringComparison.OrdinalIgnoreCase))
            return false;

        var settings = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return settings.TryGetValue("Enlist", out var value) && bool.TryParse(value as string, out var enlist) && !enlist;
    }

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

        var explicitlyEnlisted = database.GetEnlistedTransaction();
        var enlistedOrAmbient = explicitlyEnlisted ?? GetAmbientTransaction(context);
        if (explicitlyEnlisted is null && (enlistedOrAmbient ?? Transaction.Current) is not null &&
            HasAmbientEnlistmentOptOut(database))
        {
            var connection = database.GetDbConnection();
            if (connection.State == ConnectionState.Open && await TrySavepointOnOpenConnectionAsync(connection, cancellationToken))
            {
                // The callback left an actual transaction on this session. EF may have no
                // transaction record for direct ADO.NET enlistment.
                var autoTransactions = database.AutoTransactionBehavior;
                database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
                try
                {
                    await ExecuteAtSavepointAsync(alreadyCreated: true);
                }
                finally
                {
                    database.AutoTransactionBehavior = autoTransactions;
                }
                return;
            }

            // Npgsql EF 10 detects the literal "Enlist=false" and can misread equivalent spellings.
            if (enlistedOrAmbient is not null)
            {
                if (connection.State == ConnectionState.Closed)
                {
                    // A preparation query can leave only EF's ambient bookkeeping after
                    // closing the session. Clear it before opening an independent transaction.
                    var commandTimeout = database.GetCommandTimeout();
                    await context.GetService<IDbContextTransactionManager>().ResetStateAsync(cancellationToken);
                    database.SetCommandTimeout(commandTimeout);
                }

                // Hide the ambient transaction during the local write and its EF saving hooks.
                // The onSaving callback has already run in the original scope.
                using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
                await ExecuteLocallyAsync();
                suppression.Complete();
                return;
            }
        }

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

        await ExecuteLocallyAsync();

        async Task ExecuteLocallyAsync()
        {
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
        }

        async Task ExecuteAtSavepointAsync(bool alreadyCreated = false)
        {
            // Keep savepoint commands independent of cancellation of the protected work.
            if (!alreadyCreated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await database.ExecuteSqlRawAsync("SAVEPOINT " + StoreSavepoint, CancellationToken.None);
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await write(await prepare());
                await database.ExecuteSqlRawAsync("RELEASE SAVEPOINT " + StoreSavepoint, CancellationToken.None);
            }
            catch (Exception writeException)
            {
                try
                {
                    await database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT " + StoreSavepoint, CancellationToken.None);
                    await database.ExecuteSqlRawAsync("RELEASE SAVEPOINT " + StoreSavepoint, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException("Store write failed and restoring its savepoint failed.", writeException, rollbackException);
                }

                throw;
            }
        }
    }

    private static async Task<bool> TrySavepointOnOpenConnectionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var command = connection.CreateCommand();
        command.CommandText = "SAVEPOINT " + StoreSavepoint;
        try
        {
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            return true;
        }
        catch (DbException exception) when (exception.SqlState == "25P01") // No active SQL transaction.
        {
            return false;
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

        // ExecuteAsync prepares the write inside a local transaction or a borrowed savepoint.
        var database = context.Database;
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
