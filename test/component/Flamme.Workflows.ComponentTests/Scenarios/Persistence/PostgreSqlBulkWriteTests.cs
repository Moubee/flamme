using System.Diagnostics;
using System.Transactions;
using Flamme.Common.Multitenancy;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.UnitTests;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flamme.Workflows.ComponentTests.Scenarios.Persistence;

[ClassDataSource<PostgreSqlStoreSaveManyFixture>(Shared = SharedType.PerClass)]
public sealed class PostgreSqlBulkWriteTests(PostgreSqlStoreSaveManyFixture fixture)
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task BulkCallbacksUseOneContextSequentially(bool insert)
    {
        await using var scenario = await CreateScenarioAsync();
        var rows = new[] { Row("b"), Row("a"), Row("c") };
        var callbacks = new List<string>();
        OwnershipDbContext? firstContext = null;
        var activeCallbacks = 0;

        async ValueTask Prepare(OwnershipDbContext db, OwnedRow row, CancellationToken cancellationToken)
        {
            firstContext ??= db;
            await Assert.That(ReferenceEquals(db, firstContext)).IsTrue();
            var concurrent = Interlocked.Increment(ref activeCallbacks);
            try
            {
                await Assert.That(concurrent).IsEqualTo(1);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.02)", cancellationToken);
                row.Payload = $"prepared:{row.Id}";
                callbacks.Add(row.Id);
            }
            finally
            {
                Interlocked.Decrement(ref activeCallbacks);
            }
        }

        if (insert)
            await scenario.Store.AddManyAsync(rows, Prepare);
        else
            await scenario.Store.SaveManyAsync(rows, row => row.Id, Prepare);

        await Assert.That(callbacks.SequenceEqual(["b", "a", "c"])).IsTrue();
        foreach (var row in rows)
            await Assert.That((await scenario.FindAsync(row.Id))!.Payload).IsEqualTo($"prepared:{row.Id}");
    }

    [Test]
    public async Task AddManyRollsBackOnUniqueViolationAndCanBeRetried()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.Store.AddAsync(Row("z-winner", "original"));
        var rows = NewRows().Append(Row("z-winner", "loser")).ToArray();

        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => scenario.Store.AddManyAsync(rows, onSaving: null));
        await Assert.That((error!.InnerException as PostgresException)?.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await Assert.That(await scenario.FindAsync("new-000")).IsNull();
        await Assert.That((await scenario.FindAsync("z-winner"))!.Payload).IsEqualTo("original");

        await scenario.Store.AddManyAsync(NewRows(), onSaving: null);
        await Assert.That((await scenario.FindAsync("new-000"))!.Payload).IsEqualTo("initial");
    }

    [Test]
    public async Task BulkDuplicateKeysFailBeforeChunkLayoutCanAffectResult()
    {
        await using var scenario = await CreateScenarioAsync();
        var first = Row("repeated");
        var rows = new[] { first }.Concat(NewRows()).Append(Row("repeated", "second")).ToArray();

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            scenario.Store.SaveManyAsync(rows, row => row.Id, onSaving: null));
        await Assert.That(error!.Message).Contains("duplicate key");
        await Assert.That(await scenario.FindAsync("repeated")).IsNull();
        await Assert.That(await scenario.FindAsync("new-000")).IsNull();
    }

    [Test]
    public async Task LargeDistinctBulkRoundTripsWithoutAdvisoryLocks()
    {
        await using var scenario = await CreateScenarioAsync();
        const int rowCount = 10_000;
        var rows = Enumerable.Range(0, rowCount).Reverse()
            .Select(index => Row($"large-{index:00000}", $"payload-{index}"))
            .ToArray();

        using (var ambient = NewAmbientTransaction())
        {
            var writerPid = 0;
            await scenario.Store.SaveManyAsync(rows, row => row.Id, async (db, _, _) =>
            {
                if (writerPid == 0)
                    writerPid = await BackendPidAsync(db);
            });

            await using (var writer = await scenario.Store.CreateDbContextAsync())
                await Assert.That(await BackendPidAsync(writer)).IsEqualTo(writerPid);

            using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            {
                await using var observer = await scenario.Store.CreateDbContextAsync();
                var advisoryLocks = await observer.Database.SqlQuery<long>($"""
                    SELECT count(*) AS "Value" FROM pg_locks
                    WHERE pid = {writerPid} AND locktype = 'advisory'
                    """).SingleAsync();
                await Assert.That(advisoryLocks).IsEqualTo(0);
                await Assert.That(await observer.Rows.CountAsync()).IsEqualTo(0);
                Console.WriteLine($"Rows={rowCount}, writer backend={writerPid}, advisory locks before commit={advisoryLocks}.");
            }

            ambient.Complete();
        }

        await using var reader = await scenario.Store.CreateDbContextAsync();
        var persisted = await reader.Rows.AsNoTracking().ToDictionaryAsync(row => row.Id);
        await Assert.That(persisted.Count).IsEqualTo(rowCount);
        await Assert.That(rows.All(row => persisted.TryGetValue(row.Id, out var found) &&
            found.Payload == row.Payload && found.TenantId == row.TenantId)).IsTrue();
    }

    [Test]
    public async Task ConcurrentRawInsertMakesForeignBulkConflictRollBackPriorChunk()
    {
        await using var scenario = await CreateScenarioAsync();
        await using var owner = await scenario.Store.CreateDbContextAsync();
        await using var transaction = await owner.Database.BeginTransactionAsync();
        var blocker = await BackendPidAsync(owner);
        owner.Rows.Add(Row("z-contested", "winner"));
        await owner.SaveChangesAsync();

        Task write;
        using (scenario.UseTenant("tenant-b"))
        {
            var rows = NewRows("tenant-b").Append(Row("z-contested", "loser", "tenant-b")).ToArray();
            write = scenario.Store.SaveManyAsync(rows, row => row.Id, onSaving: null);
            await WaitForTransactionBlockAsync(scenario, blocker);
        }

        await Assert.That(await scenario.FindAsync("new-000")).IsNull();
        await transaction.CommitAsync();
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => write);
        await Assert.That(error!.Message).Contains("affected 2 of 3 rows");
        await Assert.That(await scenario.FindAsync("new-000")).IsNull();
        await Assert.That(await scenario.FindAsync("new-051")).IsNull();
        var winner = await scenario.FindAsync("z-contested");
        await Assert.That(winner!.Payload).IsEqualTo("winner");
        await Assert.That(winner.TenantId).IsEqualTo("tenant-a");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task BulkUpsertWaitsForUncommittedInsertAndUpdatesSameOwner(bool firstIsBulk)
    {
        await using var scenario = await CreateScenarioAsync();
        Task next;

        using (var ambient = NewAmbientTransaction())
        {
            var first = Row("z-contested", "first insert");
            if (firstIsBulk)
                await scenario.Store.SaveManyAsync([first], row => row.Id, onSaving: null);
            else
                await scenario.Store.SaveAsync(first, row => row.Id);

            int blocker;
            await using (var db = await scenario.Store.CreateDbContextAsync())
                blocker = await BackendPidAsync(db);

            using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            {
                var row = Row("z-contested", "after");
                next = scenario.Store.SaveManyAsync([row, Row("a-new")], item => item.Id, onSaving: null);
                await WaitForTransactionBlockAsync(scenario, blocker);
                await Assert.That(await scenario.FindAsync(row.Id)).IsNull();
            }

            ambient.Complete();
        }

        await next.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That((await scenario.FindAsync("z-contested"))!.Payload).IsEqualTo("after");
        await Assert.That(await scenario.FindAsync("a-new")).IsNotNull();
    }

    [Test]
    public async Task TrackedFirstSaveLosesToUncommittedBulkInsertWithoutRetryingCallback()
    {
        await using var scenario = await CreateScenarioAsync();
        Task tracked;
        var callbackCalls = 0;

        using (var ambient = NewAmbientTransaction())
        {
            await scenario.Store.SaveManyAsync([Row("contested", "bulk winner")], row => row.Id, onSaving: null);
            int blocker;
            await using (var db = await scenario.Store.CreateDbContextAsync())
                blocker = await BackendPidAsync(db);

            using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            {
                tracked = scenario.Store.SaveAsync(Row("contested", "tracked contender"), row => row.Id, (_, _, _) =>
                {
                    Interlocked.Increment(ref callbackCalls);
                    return ValueTask.CompletedTask;
                });
                await WaitForTransactionBlockAsync(scenario, blocker);
            }

            ambient.Complete();
        }

        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => tracked.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.That((error!.InnerException as PostgresException)?.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await Assert.That(callbackCalls).IsEqualTo(1);
        var winner = await scenario.FindAsync("contested");
        await Assert.That(winner!.Payload).IsEqualTo("bulk winner");
        await Assert.That(winner.TenantId).IsEqualTo("tenant-a");

        await scenario.Store.SaveAsync(Row("contested", "tracked retry"), row => row.Id);
        await Assert.That((await scenario.FindAsync("contested"))!.Payload).IsEqualTo("tracked retry");
    }

    [Test]
    public async Task FailedBulkUsesAmbientSavepointAndKeepsPreviousWrites()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.Store.AddAsync(Row("z-contested", "owner", Tenant.AgnosticTenantId));

        using (var ambient = NewAmbientTransaction())
        {
            await scenario.Store.AddAsync(Row("keep"));
            var rows = NewRows().Append(Row("z-contested", "loser")).ToArray();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                scenario.Store.SaveManyAsync(rows, row => row.Id, onSaving: null));
            await scenario.Store.AddAsync(Row("after-failure"));
            ambient.Complete();
        }

        await Assert.That(await scenario.FindAsync("keep")).IsNotNull();
        await Assert.That(await scenario.FindAsync("after-failure")).IsNotNull();
        await Assert.That(await scenario.FindAsync("new-000")).IsNull();
        await Assert.That((await scenario.FindAsync("z-contested"))!.TenantId).IsEqualTo(Tenant.AgnosticTenantId);
    }

    [Test]
    public async Task PrecancelledBulkSkipsPreparationAndWrites()
    {
        await using var scenario = await CreateScenarioAsync();
        var callbackCalls = 0;
        var cancellation = new CancellationToken(canceled: true);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            scenario.Store.SaveManyAsync([Row("cancelled")], row => row.Id, (_, _, _) =>
            {
                callbackCalls++;
                return ValueTask.CompletedTask;
            }, cancellation));

        await Assert.That(callbackCalls).IsEqualTo(0);
        await Assert.That(await scenario.FindAsync("cancelled")).IsNull();
    }

    [Test]
    public async Task CancellingDuringLaterChunkRollsBackAndAllowsRetry()
    {
        await using var scenario = await CreateScenarioAsync();
        await using var owner = await scenario.Store.CreateDbContextAsync();
        await using var transaction = await owner.Database.BeginTransactionAsync();
        var blocker = await BackendPidAsync(owner);
        owner.Rows.Add(Row("z-contested", "uncommitted"));
        await owner.SaveChangesAsync();

        using var cancellation = new CancellationTokenSource();
        var rows = NewRows().Append(Row("z-contested", "retry")).ToArray();
        var write = scenario.Store.SaveManyAsync(rows, row => row.Id, cancellation.Token);
        await WaitForTransactionBlockAsync(scenario, blocker);
        await cancellation.CancelAsync();

        try
        {
            await write.WaitAsync(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException("Bulk write ignored cancellation.");
        }
        catch (OperationCanceledException)
        {
            await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        }

        await transaction.RollbackAsync();
        await Assert.That(await scenario.FindAsync("new-000")).IsNull();
        await scenario.Store.SaveManyAsync(rows, row => row.Id, onSaving: null);
        await Assert.That((await scenario.FindAsync("z-contested"))!.Payload).IsEqualTo("retry");
    }

    [Test]
    public async Task NondeterministicKeyCollationDoesNotPermitSpellingVariantToOverwrite()
    {
        await using var scenario = await CreateScenarioAsync();
        await using (var db = await scenario.Store.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE COLLATION "Elsa"."bulk_ci" (provider = icu, locale = 'und-u-ks-level2', deterministic = false);
                ALTER TABLE "Elsa"."OwnedRows" ALTER COLUMN "Id" TYPE text COLLATE "Elsa"."bulk_ci";
                """);
        }
        await scenario.Store.AddAsync(Row("abc", "stored"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            scenario.Store.SaveManyAsync([Row("abc", "permitted alone"), Row("ABC", "collision"), Row("other")], row => row.Id, onSaving: null));

        var stored = await scenario.FindAsync("abc");
        await Assert.That(stored!.Id).IsEqualTo("abc");
        await Assert.That(stored.Payload).IsEqualTo("stored");
        await Assert.That(await scenario.FindAsync("other")).IsNull();
    }

    [Test]
    public async Task ActualDeadlockRestoresBorrowedTransactionAndVictimCanRetry()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.Store.AddManyAsync([Row("a"), Row("z")], onSaving: null);
        await using var first = await scenario.Store.CreateDbContextAsync();
        await using var second = await scenario.Store.CreateDbContextAsync();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();

        await first.Database.ExecuteSqlRawAsync("""SELECT "Id" FROM "Elsa"."OwnedRows" WHERE "Id" = 'a' FOR UPDATE""");
        await second.Database.ExecuteSqlRawAsync("""SELECT "Id" FROM "Elsa"."OwnedRows" WHERE "Id" = 'z' FOR UPDATE""");

        var firstRows = new[] { Row("0-first"), Row("z", "first") };
        var secondRows = new[] { Row("0-second"), Row("a", "second") };
        var firstWrite = first.BulkUpsertAsync(firstRows, row => row.Id, batchSize: 1);
        await WaitForTransactionBlockAsync(scenario, await BackendPidAsync(second));
        var secondWrite = second.BulkUpsertAsync(secondRows, row => row.Id, batchSize: 1);

        var victim = await Task.WhenAny(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => victim);
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.DeadlockDetected);

        var firstVictim = ReferenceEquals(victim, firstWrite);
        var victimContext = firstVictim ? first : second;
        var victimRows = firstVictim ? firstRows : secondRows;
        var victimTransaction = firstVictim ? firstTransaction : secondTransaction;
        var survivorTransaction = firstVictim ? secondTransaction : firstTransaction;
        // A statement after 40P01 also checks that rollback to the savepoint cleared the aborted state.
        await Assert.That(await BackendPidAsync(victimContext)).IsGreaterThan(0);
        await victimTransaction.RollbackAsync();
        await (firstVictim ? secondWrite : firstWrite).WaitAsync(TimeSpan.FromSeconds(10));
        await survivorTransaction.CommitAsync();
        await Assert.That(await scenario.FindAsync(victimRows[0].Id)).IsNull();

        await scenario.Store.SaveManyAsync(victimRows, row => row.Id, onSaving: null);
        await Assert.That(await scenario.FindAsync(victimRows[0].Id)).IsNotNull();
    }

    private Task<OwnershipStoreScenario> CreateScenarioAsync() =>
        OwnershipStoreScenario.CreatePostgreSqlAsync(fixture.ConnectionString, "tenant-a", tenantsEnabled: true);

    private static OwnedRow Row(string id, string payload = "initial", string tenant = "tenant-a") =>
        new() { Id = id, TenantId = tenant, Payload = payload };

    private static IEnumerable<OwnedRow> NewRows(string tenant = "tenant-a") =>
        Enumerable.Range(0, 52).Select(index => Row($"new-{index:000}", tenant: tenant));

    private static TransactionScope NewAmbientTransaction() =>
        new(TransactionScopeOption.RequiresNew, new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

    private static Task<int> BackendPidAsync(OwnershipDbContext db) =>
        db.Database.SqlQueryRaw<int>("""SELECT pg_backend_pid() AS "Value" """).SingleAsync();

    private static async Task WaitForTransactionBlockAsync(OwnershipStoreScenario scenario, int blocker)
    {
        await using var db = await scenario.Store.CreateDbContextAsync();
        var elapsed = Stopwatch.StartNew();
        while (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event = 'transactionid'
                  AND {blocker} = ANY(pg_blocking_pids(pid))
            ) AS "Value"
            """).SingleAsync())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(8))
                throw new TimeoutException($"No PostgreSQL session waited for a transaction held by backend {blocker}.");
            await Task.Delay(15);
        }
    }
}
