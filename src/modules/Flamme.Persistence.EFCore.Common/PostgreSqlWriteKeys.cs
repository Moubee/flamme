using System.Globalization;
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

    internal static async Task ExecuteAsync(DbContext context, Func<Task> operation, CancellationToken cancellationToken)
    {
        var database = context.Database;
        if (database.CurrentTransaction is not null)
        {
            await operation();
            return;
        }

        if (Transaction.Current is not null || database.GetEnlistedTransaction() is not null)
        {
            // Keep the lock and subsequent EF commands on the same enlisted connection.
            await database.OpenConnectionAsync(cancellationToken);
            try
            {
                if (database.GetEnlistedTransaction() is null && Transaction.Current is { } ambient)
                    database.EnlistTransaction(ambient);

                await operation();
            }
            finally
            {
                await database.CloseConnectionAsync();
            }
            return;
        }

        if (database.CreateExecutionStrategy().RetriesOnFailure)
        {
            // This transaction is one unit. The Store cannot replay arbitrary saving hooks
            // or determine if an interrupted COMMIT already succeeded.
            await new SingleAttemptTransactionStrategy(context).ExecuteAsync(ExecuteLocalTransactionAsync, cancellationToken);
            return;
        }

        await ExecuteLocalTransactionAsync(cancellationToken);

        async Task ExecuteLocalTransactionAsync(CancellationToken ct)
        {
            // A subsequent read must see the write that released a contended key.
            await using var transaction = await database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            await operation();
            await transaction.CommitAsync(ct);
        }
    }

    internal static Task AcquireAsync(
        DbContext context,
        IProperty keyProperty,
        StoreObjectIdentifier table,
        string? key,
        CancellationToken cancellationToken)
    {
        if (key == null)
            return Task.CompletedTask;

        var database = context.Database;
        if (database.CurrentTransaction is null && database.GetEnlistedTransaction() is null)
            throw new InvalidOperationException("PostgreSQL write keys require an active database transaction.");

        var sql = context.GetService<ISqlGenerationHelper>();
        var tableName = sql.DelimitIdentifier(table.Name, table.Schema);
        var column = keyProperty.GetColumnName(table)!;
        var providerKey = GetProviderKey(keyProperty, key);

        // Resolve the table to its OID so EF contexts addressing the same table agree even if
        // one mapping omits its schema.
        return database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_catalog.pg_advisory_xact_lock(
                pg_catalog.hashtextextended(
                    {providerKey} COLLATE pg_catalog."C",
                    pg_catalog.hashtextextended(
                        concat('flamme:store:', pg_catalog.to_regclass({tableName})::oid, ':', {column}), 0)))
            """, cancellationToken);
    }

    private sealed class SingleAttemptTransactionStrategy(DbContext context)
        : ExecutionStrategy(context, maxRetryCount: 0, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
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
