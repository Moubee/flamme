using System.Data.Common;
using System.Text;
using System.Transactions;
using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.EntityHandlers;
using Flamme.Tenants.Options;
using Flamme.Testing.Shared.Databases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TUnit.Core.Interfaces;

namespace Flamme.Workflows.ComponentTests.Scenarios.Persistence;

[ClassDataSource<PostgreSqlTrackedSaveFixture>(Shared = SharedType.PerClass)]
[NotInParallel("StoreSave:PostgreSQL")]
public sealed class PostgreSqlTrackedSaveTests(PostgreSqlTrackedSaveFixture fixture)
{
    [Test]
    public async Task ConcurrentFirstSavesOfTheSameMappedKeyBothPersist()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var id = NewId();
        var secondCallbackCount = 0;
        var backend = fixture.SaveInHeldTransaction(backend => fixture.SaveWithShadowAsync(NewRow(id, "first"), "one", token, backend), token);
        await using var holder = backend;
        var heldPid = await holder.Ready;
        var second = fixture.Store.SaveAsync(NewRow(id.ToUpperInvariant(), "second"), row => row.Id, async (context, row, ct) =>
        {
            Interlocked.Increment(ref secondCallbackCount);
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            context.Entry(row).Property("ShadowText").CurrentValue = "two";
        }, token);

        await fixture.WaitForBlockedWriter(heldPid, "advisory", token);
        await holder.ReleaseAsync();
        await second;
        var stored = await fixture.ReadAsync(id, token);
        var raw = await fixture.ReadScalarAsync($"SELECT \"Payload\" FROM {fixture.Table} WHERE \"Id\" = $1", id.ToUpperInvariant(), token);
        await Assert.That(secondCallbackCount).IsEqualTo(1);
        await Assert.That(stored.Owner).IsEqualTo("a");
        await Assert.That(stored.Payload).IsEqualTo("second");
        await Assert.That(stored.Shadow).IsEqualTo("two");
        await Assert.That(stored.Hook).IsEqualTo("saved:second");
        await Assert.That(raw).IsEqualTo("SECOND");
    }

    [Test]
    public async Task NativeBulkFirstInsertPreservesWinnerAndReportsTrackedUniqueConflict()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var id = NewId();

        await using var holder = fixture.SaveInHeldTransaction(
            backend => fixture.SaveBulkAndReadBackendAsync(NewRow(id, "bulk"), backend, token), token);
        var heldPid = await holder.Ready;
        var callbacks = 0;
        var tracked = fixture.Store.SaveAsync(NewRow(id, "tracked"), row => row.Id, (context, row, _) =>
        {
            Interlocked.Increment(ref callbacks);
            context.Entry(row).Property("ShadowText").CurrentValue = "discard";
            return ValueTask.CompletedTask;
        }, token);
        await fixture.WaitForBlockedWriter(heldPid, "transactionid", token);
        await holder.ReleaseAsync();

        var failure = await FailureAsync(tracked);
        var sqlState = (failure?.InnerException as PostgresException)?.SqlState;
        var stored = await fixture.ReadAsync(id, token);
        await Assert.That(sqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await Assert.That(callbacks).IsEqualTo(1);
        await Assert.That(stored.Owner).IsEqualTo("a");
        await Assert.That(stored.Payload).IsEqualTo("bulk");
        await Assert.That(stored.Shadow).IsNull();
    }

    [Test]
    public async Task NativeBulkUpsertCanFollowTrackedFirstInsert()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var id = NewId();
        await using var holder = fixture.SaveInHeldTransaction(
            backend => fixture.SaveWithShadowAsync(NewRow(id, "tracked"), "tracked", token, backend), token);
        var heldPid = await holder.Ready;
        var bulk = fixture.Store.SaveManyAsync([NewRow(id, "bulk")], row => row.Id, cancellationToken: token);
        await fixture.WaitForBlockedWriter(heldPid, "transactionid", token);
        await holder.ReleaseAsync();
        await bulk;

        var stored = await fixture.ReadAsync(id, token);
        await Assert.That(stored.Owner).IsEqualTo("a");
        await Assert.That(stored.Payload).IsEqualTo("bulk");
    }

    [Test]
    public async Task TenantCannotReplaceAnotherOwnersRowThroughEitherSavePath()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var originalTenant = fixture.UseTenant("a");
        var id = NewId();
        var otherId = NewId();

        await using var holder = fixture.SaveInHeldTransaction(
            backend => fixture.SaveWithShadowAsync(NewRow(id, "original"), "owned", token, backend), token);
        var heldPid = await holder.Ready;
        using (fixture.UseTenant("b"))
        {
            var bulk = fixture.Store.SaveManyAsync([NewRow(otherId, "other"), NewRow(id, "foreign")], row => row.Id, cancellationToken: token);
            await fixture.WaitForBlockedWriter(heldPid, "transactionid", token);
            await holder.ReleaseAsync();
            await Assert.That(await FailureAsync(bulk)).IsTypeOf<InvalidOperationException>();
            await Assert.That(await FailureAsync(fixture.Store.SaveAsync(NewRow(id, "foreign"), row => row.Id, token)))
                .IsTypeOf<DbUpdateException>();
        }

        var forged = NewRow(id, "forged");
        forged.TenantId = "b";
        await Assert.That(await FailureAsync(fixture.Store.SaveAsync(forged, row => row.Id, token))).IsTypeOf<InvalidOperationException>();
        var stored = await fixture.ReadAsync(id, token);
        await Assert.That(stored.Owner).IsEqualTo("a");
        await Assert.That(stored.Shadow).IsEqualTo("owned");
        await Assert.That(await fixture.CountAsync([otherId], "other", token)).IsEqualTo(0);
    }

    [Test]
    public async Task CancellingKeyWaitLeavesTheNextSaveUsable()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var id = NewId();
        await using var holder = fixture.SaveInHeldTransaction(
            backend => fixture.SaveWithShadowAsync(NewRow(id, "original"), "held", token, backend), token);
        var heldPid = await holder.Ready;

        var waiting = fixture.SaveWithShadowAsync(NewRow(id, "cancelled"), "discard", cancel.Token);
        await fixture.WaitForBlockedWriter(heldPid, "advisory", token);
        await cancel.CancelAsync();
        await Assert.That(await FailureAsync(waiting)).IsAssignableTo<OperationCanceledException>();

        await holder.ReleaseAsync();
        await Assert.That((await fixture.ReadAsync(id, token)).Payload).IsEqualTo("original");
        await fixture.SaveWithShadowAsync(NewRow(id, "recovery"), "recovered", token);
        await Assert.That((await fixture.ReadAsync(id, token)).Payload).IsEqualTo("recovery");
    }

    [Test]
    public async Task HeldSaveLetsFortyEightIndependentRowsProgress()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var ids = Enumerable.Range(0, 49).Select(_ => NewId()).ToArray();
        await fixture.Store.AddManyAsync(ids.Select(id => NewRow(id, "seed")), token);

        await using var blocker = await fixture.OpenObserverAsync(token);
        await using var transaction = await blocker.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand($"SELECT \"Id\" FROM {fixture.Table} WHERE \"Id\" = $1 FOR UPDATE", blocker, transaction);
        command.Parameters.Add(new NpgsqlParameter { Value = ids[0].ToUpperInvariant() });
        await command.ExecuteScalarAsync(token);
        var hot = fixture.SaveWithShadowAsync(NewRow(ids[0], "hot"), "hot", token);
        await fixture.WaitForBlockedWriter(blocker.ProcessID, "transactionid", token);

        var cold = Task.WhenAll(ids.Skip(1).Select(id => fixture.SaveWithShadowAsync(NewRow(id, "cold"), "cold", token)));
        await cold.WaitAsync(token);
        var persisted = await fixture.CountAsync(ids[1..], "cold", token);
        var hotBeforeRelease = hot.IsCompleted;

        await transaction.RollbackAsync(token);
        await hot;
        await Assert.That(hotBeforeRelease).IsFalse();
        await Assert.That(persisted).IsEqualTo(48);
        await Assert.That((await fixture.ReadAsync(ids[0], token)).Payload).IsEqualTo("hot");
    }

    [Test]
    public async Task AddManyRunsCallbacksInOrderAndPersistsTrackedShadowProperties()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var rows = Enumerable.Range(0, 4).Select(_ => NewRow(NewId(), "batch")).ToArray();
        var callbacks = new List<string>();
        int active = 0;
        await fixture.Store.AddManyAsync(rows, async (context, row, ct) =>
        {
            if (Interlocked.Increment(ref active) != 1)
                throw new InvalidOperationException("Callbacks on the shared context overlapped.");
            await Task.Delay(1, ct);
            context.Entry(row).Property("ShadowText").CurrentValue = row.Id;
            callbacks.Add(row.Id);
            Interlocked.Decrement(ref active);
        }, token);

        await Assert.That(callbacks.SequenceEqual(rows.Select(row => row.Id))).IsTrue();
        foreach (var row in rows)
        {
            var stored = await fixture.ReadAsync(row.Id, token);
            await Assert.That(stored.Shadow).IsEqualTo(row.Id);
            await Assert.That(stored.Hook).IsEqualTo("saved:batch");
        }
    }

    [Test]
    public async Task NonparticipatingCreateLeavesItsUniqueRowIntact()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var id = NewId();

        await using var holder = fixture.SaveInHeldTransaction(backend => fixture.Store.AddAsync(
            NewRow(id, "explicit-create"), (context, _, ct) => fixture.ReadBackendAsync(context, backend, ct), token), token);
        var heldPid = await holder.Ready;
        var waiting = fixture.SaveWithShadowAsync(NewRow(id, "tracked"), "discard", token);
        await fixture.WaitForBlockedWriter(heldPid, "transactionid", token);
        await holder.ReleaseAsync();

        var failure = await FailureAsync(waiting);
        var sqlState = (failure?.InnerException as PostgresException)?.SqlState;
        await Assert.That(sqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await Assert.That((await fixture.ReadAsync(id, token)).Payload).IsEqualTo("explicit-create");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BulkRejectsDuplicateMappedKeys(bool crossChunkBoundary)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var prefix = NewId();
        var id = $"{prefix}-target";
        var mappedId = id.ToUpperInvariant();
        // CLR ordinal sorting puts the uppercase alias before the fillers and
        // the lowercase alias after them, beyond the default 50-row chunk.
        var fillers = Enumerable.Range(0, crossChunkBoundary ? 50 : 0)
            .Select(index => NewRow($"{prefix}-item-{index:00}", "filler")).ToArray();
        var rows = fillers.Prepend(NewRow(mappedId, "first")).Append(NewRow(id, "second")).ToArray();

        var failure = await FailureAsync(fixture.Store.SaveManyAsync(rows, row => row.Id, cancellationToken: token));
        var stored = await fixture.ReadScalarAsync($"SELECT \"Payload\" FROM {fixture.Table} WHERE \"Id\" = $1", mappedId, token);
        await Assert.That(stored).IsNull();
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("duplicate key");
        await Assert.That(await fixture.CountAsync(fillers.Select(row => row.Id).ToArray(), "filler", token)).IsEqualTo(0);
    }

    [Test]
    public async Task BulkRejectsStructurallyEqualProviderKeys()
    {
        using var tenant = fixture.UseTenant("a");
        var id = NewId();
        var first = new MappedBulkRow { Id = id, UniqueValue = $"{id}-first" };
        var second = new MappedBulkRow { Id = id.ToUpperInvariant(), UniqueValue = $"{id}-second" };

        var failure = await FailureAsync(fixture.MappedStore.SaveManyAsync([first, second], row => row.Id, onSaving: null));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("duplicate key");
        await using var db = await fixture.MappedStore.CreateDbContextAsync();
        await Assert.That(await db.Set<MappedBulkRow>().IgnoreQueryFilters().AnyAsync(row => row.Id == id)).IsFalse();
    }

    [Test]
    public async Task BulkUsesSamePhysicalLockOrderForNormalizedKeys()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var prefix = NewId();
        var a = $"{prefix}-a";
        var b = $"{prefix}-b";
        await fixture.Store.SaveManyAsync([NewRow(a, "seed"), NewRow(b, "seed")], row => row.Id, cancellationToken: token);

        await using var blocker = await fixture.OpenObserverAsync(token);
        await using var transaction = await blocker.BeginTransactionAsync(token);
        await using var lockRow = new NpgsqlCommand($"SELECT 1 FROM {fixture.Table} WHERE \"Id\" = $1 FOR UPDATE", blocker, transaction);
        lockRow.Parameters.Add(new NpgsqlParameter { Value = b.ToUpperInvariant() });
        await lockRow.ExecuteNonQueryAsync(token);

        var backend = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.Store.SaveManyAsync([NewRow(b.ToUpperInvariant(), "first"), NewRow(a, "first")], row => row.Id,
            (db, _, ct) => backend.Task.IsCompleted ? ValueTask.CompletedTask : fixture.ReadBackendAsync(db, backend, ct), token);
        var second = Task.CompletedTask;
        try
        {
            var firstPid = await backend.Task.WaitAsync(token);
            await fixture.WaitForBlockedWriter(blocker.ProcessID, "transactionid", token);

            // The first batch has already written A while it waits to write B.
            await using var observer = await fixture.OpenObserverAsync(token);
            await using var probe = new NpgsqlCommand($"SELECT 1 FROM {fixture.Table} WHERE \"Id\" = $1 FOR UPDATE NOWAIT", observer);
            probe.Parameters.Add(new NpgsqlParameter { Value = a.ToUpperInvariant() });
            var locked = await Assert.ThrowsExactlyAsync<PostgresException>(() => probe.ExecuteNonQueryAsync(token));
            await Assert.That(locked!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);

            second = fixture.Store.SaveManyAsync([NewRow(a.ToUpperInvariant(), "second"), NewRow(b, "second")],
                row => row.Id, cancellationToken: token);
            await fixture.WaitForBlockedWriter(firstPid, "transactionid", token);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await Task.WhenAll(first, second).WaitAsync(token);
        }

        await Assert.That(await fixture.CountAsync([a, b], "second", token)).IsEqualTo(2);
    }

    [Test]
    public async Task BulkReleasesAndReusesSecondaryUniqueValueInCallerOrder()
    {
        using var tenant = fixture.UseTenant("a");
        var prefix = NewId();
        var occupied = $"{prefix}-occupied";
        var release = new MappedBulkRow { Id = $"{prefix}-z", UniqueValue = occupied };
        await fixture.MappedStore.SaveManyAsync([release], row => row.Id, onSaving: null);
        var take = new MappedBulkRow { Id = $"{prefix}-a", UniqueValue = occupied };
        release.UniqueValue = $"{prefix}-vacated";

        await fixture.MappedStore.SaveManyAsync([release, take], row => row.Id, onSaving: null);

        await using var db = await fixture.MappedStore.CreateDbContextAsync();
        var stored = await db.Set<MappedBulkRow>().IgnoreQueryFilters().Where(row => row.Id == release.Id || row.Id == take.Id)
            .ToDictionaryAsync(row => row.Id, row => row.UniqueValue);
        await Assert.That(stored[release.Id]).IsEqualTo(release.UniqueValue);
        await Assert.That(stored[take.Id]).IsEqualTo(occupied);
    }

    [Test]
    [Arguments("a", "a", false)]
    [Arguments("a", Tenant.AgnosticTenantId, false)]
    [Arguments(Tenant.DefaultTenantId, Tenant.DefaultTenantId, false)]
    [Arguments(Tenant.DefaultTenantId, Tenant.DefaultTenantId, true)]
    public async Task BulkPreservesConvertedTenantOwnership(string writer, string owner, bool legacyNull)
    {
        using var tenant = fixture.UseTenant(writer);
        var id = NewId();
        var row = new MappedBulkRow { Id = id, TenantId = owner, UniqueValue = $"{id}-original" };
        await fixture.MappedStore.SaveManyAsync([row], item => item.Id, onSaving: null);
        await using var db = await fixture.MappedStore.CreateDbContextAsync();
        var stored = db.Set<MappedBulkRow>().IgnoreQueryFilters().Where(item => item.Id == id);
        if (legacyNull)
        {
            var changed = await stored.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.TenantId, (string?)null));
            await Assert.That(changed).IsEqualTo(1);
        }

        row.UniqueValue = $"{id}-updated";
        await fixture.MappedStore.SaveManyAsync([row], item => item.Id, onSaving: null);

        var updated = await stored.AsNoTracking().SingleAsync();
        await Assert.That(updated.TenantId).IsEqualTo(owner);
        await Assert.That(updated.UniqueValue).IsEqualTo(row.UniqueValue);
    }

    [Test]
    public async Task BulkRejectsReplacementByDifferentTenantWithConvertedOwner()
    {
        var id = NewId();
        var row = new MappedBulkRow { Id = id, TenantId = "a", UniqueValue = $"{id}-original" };
        using (fixture.UseTenant("a"))
            await fixture.MappedStore.SaveManyAsync([row], item => item.Id, onSaving: null);

        row.UniqueValue = $"{id}-forged";
        using (fixture.UseTenant("b"))
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                fixture.MappedStore.SaveManyAsync([row], item => item.Id, onSaving: null));

        await using var db = await fixture.MappedStore.CreateDbContextAsync();
        var stored = await db.Set<MappedBulkRow>().IgnoreQueryFilters().SingleAsync(item => item.Id == id);
        await Assert.That(stored.TenantId).IsEqualTo("a");
        await Assert.That(stored.UniqueValue).IsEqualTo($"{id}-original");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task NativeBatchAcquiresNoAdvisoryLocks(bool insertOnly)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var rows = Enumerable.Range(0, 256).Select(_ => NewRow(NewId(), "bulk")).ToArray();
        await using var holder = fixture.SaveInHeldTransaction(backend =>
        {
            ValueTask CaptureBackend(TrackedSaveContext context, TrackedSaveRow _, CancellationToken ct) =>
                backend.Task.IsCompleted ? ValueTask.CompletedTask : fixture.ReadBackendAsync(context, backend, ct);

            return insertOnly
                ? fixture.Store.AddManyAsync(rows, CaptureBackend, token)
                : fixture.Store.SaveManyAsync(rows, row => row.Id, CaptureBackend, token);
        }, token);

        var heldPid = await holder.Ready;
        await Assert.That(await fixture.CountAdvisoryLocksAsync(heldPid, token)).IsEqualTo(0);
        await holder.ReleaseAsync();
        await Assert.That(await fixture.CountAsync(rows.Select(row => row.Id).ToArray(), "bulk", token)).IsEqualTo(rows.Length);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task BorrowedTransactionCanCommitAfterAdvisoryWaitFails(bool cancelWait)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var waitingCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var heldId = NewId();
        var priorId = NewId();
        var laterId = NewId();
        await using var holder = fixture.SaveInHeldTransaction(
            backend => fixture.SaveWithShadowAsync(NewRow(heldId, "winner"), "held", token, backend), token);
        var heldPid = await holder.Ready;

        var contender = Task.Run(async () =>
        {
            using var scope = BeginAmbientTransaction(IsolationLevel.ReadCommitted);
            await fixture.SaveWithShadowAsync(NewRow(priorId, "prior"), "prior", token);
            var failure = await FailureAsync(fixture.Store.SaveAsync(NewRow(heldId, "blocked"), row => row.Id,
                async (context, _, ct) =>
                {
                    if (!cancelWait)
                        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '4s'", ct);
                }, waitingCancellation.Token));
            await fixture.SaveWithShadowAsync(NewRow(laterId, "later"), "later", token);
            scope.Complete();
            return failure;
        }, token);

        await fixture.WaitForBlockedWriter(heldPid, "advisory", token);
        if (cancelWait)
            await waitingCancellation.CancelAsync();
        var failure = await contender.WaitAsync(token);

        if (cancelWait)
            await Assert.That(failure).IsAssignableTo<OperationCanceledException>();
        else
            await Assert.That((failure?.GetBaseException() as PostgresException)?.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await Assert.That((await fixture.ReadAsync(priorId, token)).Payload).IsEqualTo("prior");
        await Assert.That((await fixture.ReadAsync(laterId, token)).Payload).IsEqualTo("later");
        await holder.ReleaseAsync();
        await Assert.That((await fixture.ReadAsync(heldId, token)).Payload).IsEqualTo("winner");
    }

    [Test]
    [Arguments(IsolationLevel.ReadCommitted, "read committed")]
    [Arguments(IsolationLevel.RepeatableRead, "repeatable read")]
    [Arguments(IsolationLevel.Serializable, "serializable")]
    public async Task AdvisoryWaitPreservesTheCallersSnapshot(IsolationLevel isolation, string expectedIsolation)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var id = NewId();
        await using var holder = fixture.SaveInHeldTransaction(
            backend => fixture.SaveWithShadowAsync(NewRow(id, "first"), "first", token, backend), token);
        var heldPid = await holder.Ready;
        string? observedIsolation = null;
        bool? visibleBeforeWait = null;
        var contender = Task.Run(async () =>
        {
            using var scope = BeginAmbientTransaction(isolation);
            await fixture.Store.SaveAsync(NewRow(id, "second"), row => row.Id, async (context, _, ct) =>
            {
                observedIsolation = await context.Database.SqlQueryRaw<string>(
                    """SELECT current_setting('transaction_isolation') AS "Value" """).SingleAsync(ct);
                visibleBeforeWait = await context.Set<TrackedSaveRow>().AnyAsync(row => row.Id == id, ct);
            }, token);
            scope.Complete();
        }, token);

        await fixture.WaitForBlockedWriter(heldPid, "advisory", token);
        await holder.ReleaseAsync();
        var failure = await FailureAsync(contender);
        await Assert.That(observedIsolation).IsEqualTo(expectedIsolation);
        await Assert.That(visibleBeforeWait).IsEqualTo(false);

        if (isolation == IsolationLevel.ReadCommitted)
        {
            await Assert.That(failure).IsNull();
        }
        else
        {
            var sqlState = (failure?.GetBaseException() as PostgresException)?.SqlState;
            await Assert.That(sqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure).IsTrue();
            await Assert.That((await fixture.ReadAsync(id, token)).Payload).IsEqualTo("first");

            using var retry = BeginAmbientTransaction(isolation);
            await fixture.SaveWithShadowAsync(NewRow(id, "second"), "retry", token);
            retry.Complete();
        }

        await Assert.That((await fixture.ReadAsync(id, token)).Payload).IsEqualTo("second");
    }

    [Test]
    [Arguments("Enlist=true", true, false, false)]
    [Arguments("Enlist=false", false, false, false)]
    [Arguments("Enlist = false", false, false, false)]
    [Arguments("Enlist=true", true, true, false)]
    [Arguments("Enlist=false", false, true, false)]
    [Arguments("Enlist = false", false, true, false)]
    [Arguments("Enlist = false", false, false, true)]
    [Arguments("Enlist = false", false, true, true)]
    public Task WriterHonorsConfiguredAmbientEnlistment(string enlistment, bool enlist, bool bulk, bool retry) =>
        AssertWriterAmbientOutcomeAsync(fixture.CreateStore(enlistment: enlistment, retry: retry).Store, enlist, bulk);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task OpenWriterPreservesExplicitEnlistmentOrOptOut(bool explicitlyEnlist, bool bulk) =>
        AssertWriterAmbientOutcomeAsync(fixture.CreateStore(enlistment: "Enlist = false").Store, explicitlyEnlist, bulk, async (context, _, ct) =>
        {
            // Use the provider API to open first, then enlist explicitly if requested.
            await context.Database.GetDbConnection().OpenAsync(ct);
            if (explicitlyEnlist)
                context.Database.EnlistTransaction(Transaction.Current);
        });

    [Test]
    public async Task OptedOutCallerConnectionStaysReusableAfterSecurityInfoIsHidden()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var connection = fixture.CreateOptedOutConnection();
        await connection.OpenAsync(deadline.Token);
        await connection.CloseAsync();
        var initialSettings = connection.ConnectionString;
        var (_, store) = fixture.CreateStore(options => options.UseNpgsql(connection));

        await AssertWriterAmbientOutcomeAsync(store, enlist: false, bulk: false);

        await Assert.That(string.Equals(initialSettings, connection.ConnectionString, StringComparison.Ordinal)).IsTrue();
        await connection.OpenAsync(deadline.Token);
    }

    [Test]
    public async Task OptedOutCallerDataSourceStaysReusable()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var dataSource = fixture.CreateOptedOutDataSource();
        var (_, store) = fixture.CreateStore(options => options.UseNpgsql(dataSource));

        await AssertWriterAmbientOutcomeAsync(store, enlist: false, bulk: true);

        await using var connection = await dataSource.OpenConnectionAsync(deadline.Token);
    }

    [Test]
    [Arguments("Enlist=false", true)]
    [Arguments("Enlist = false", false)]
    public async Task OnlyMisdetectedOptOutSuppressesAmbientDuringSaving(string enlistment, bool expectedDuringSave)
    {
        var probe = new SavingAmbientObserver();
        var (_, store) = fixture.CreateStore(enlistment: enlistment, interceptor: probe);
        bool? ambientDuringPreparation = null;

        await AssertWriterAmbientOutcomeAsync(store, enlist: false, bulk: false, (_, _, _) =>
        {
            ambientDuringPreparation = Transaction.Current is not null;
            return ValueTask.CompletedTask;
        });

        await Assert.That(ambientDuringPreparation).IsEqualTo(true);
        await Assert.That(probe.AmbientDuringSave).IsEqualTo(expectedDuringSave);
    }

    private async Task AssertWriterAmbientOutcomeAsync(
        Store<TrackedSaveContext, TrackedSaveRow> store, bool enlist, bool bulk,
        Func<TrackedSaveContext, TrackedSaveRow, CancellationToken, ValueTask>? prepare = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var id = NewId();
        var sql = $"SELECT \"Payload\" FROM {fixture.Table} WHERE \"Id\" = $1";
        var providerId = id.ToUpperInvariant();
        var expected = enlist ? null : "WRITTEN";
        string? beforeRollback;

        using (BeginAmbientTransaction(IsolationLevel.ReadCommitted))
        {
            var ambient = Transaction.Current;
            var row = NewRow(id, "written");
            if (bulk)
                await store.SaveManyAsync([row], item => item.Id, prepare, token);
            else
                await store.SaveAsync(row, item => item.Id, prepare, token);

            await Assert.That(Transaction.Current).IsEqualTo(ambient);
            beforeRollback = await fixture.ReadScalarAsync(sql, providerId, token);
        }

        var afterRollback = await fixture.ReadScalarAsync(sql, providerId, token);
        await Assert.That((beforeRollback, afterRollback)).IsEqualTo((expected, expected));
    }

    [Test]
    public async Task TransientExistenceReadRetriesInAFreshTransactionWithoutPreparingAgain()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var failure = new FirstExistenceReadTimeout(fixture.Table);
        var (_, store) = fixture.CreateStore(retry: true, interceptor: failure);
        var id = NewId();
        var callbacks = 0;

        await store.SaveAsync(NewRow(id, "written"), item => item.Id, (context, item, _) =>
        {
            callbacks++;
            context.Entry(item).Property("ShadowText").CurrentValue = "prepared";
            return ValueTask.CompletedTask;
        }, token);

        await Assert.That(callbacks).IsEqualTo(1);
        await Assert.That(failure.TransactionIds.Count).IsEqualTo(2);
        await Assert.That(failure.TransactionIds[0]).IsNotNull();
        await Assert.That(failure.TransactionIds[1]).IsNotNull();
        await Assert.That(failure.TransactionIds[0]).IsNotEqualTo(failure.TransactionIds[1]);
        var stored = await fixture.ReadAsync(id, token);
        await Assert.That(stored.Shadow).IsEqualTo("prepared");
        await Assert.That(stored.Hook).IsEqualTo("saved:written");
    }

    [Test]
    public async Task TransientFailureReportedAfterCommitDoesNotReplayTheWrite()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var failure = new FirstCommitAcknowledgmentTimeout();
        var (_, store) = fixture.CreateStore(retry: true, interceptor: failure);
        var id = NewId();
        var callbacks = 0;

        var exception = await FailureAsync(store.SaveAsync(NewRow(id, "written"), item => item.Id, (_, _, _) =>
        {
            callbacks++;
            return ValueTask.CompletedTask;
        }, token));

        await Assert.That(exception).IsTypeOf<TimeoutException>();
        await Assert.That(failure.Commits).IsEqualTo(1);
        await Assert.That(callbacks).IsEqualTo(1);
        await Assert.That((await fixture.ReadAsync(id, token)).Payload).IsEqualTo("written");
    }

    [Test]
    public async Task RetryingProviderAcceptsLocalWriteAndCallerWrappedAmbientTransaction()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var (factory, store) = fixture.CreateStore(retry: true);
        var local = NewId();
        await store.SaveAsync(NewRow(local, "local"), row => row.Id, token);
        await Assert.That((await fixture.ReadAsync(local, token)).Payload).IsEqualTo("local");

        var external = NewId();
        await using var strategyContext = await factory.CreateDbContextAsync(token);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            using var scope = new TransactionScope(TransactionScopeOption.RequiresNew,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);
            await store.SaveAsync(NewRow(external, "ambient"), row => row.Id, ct);
            scope.Complete();
        }, token);
        await Assert.That((await fixture.ReadAsync(external, token)).Payload).IsEqualTo("ambient");

        var unwrapped = NewId();
        using (new TransactionScope(TransactionScopeOption.RequiresNew,
                   new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled))
        {
            var failure = await FailureAsync(store.SaveAsync(NewRow(unwrapped, "unsupported"), row => row.Id, token));
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(failure?.Message).Contains("does not support user-initiated transactions");
        }
        await Assert.That(await fixture.CountAsync([unwrapped], "unsupported", token)).IsEqualTo(0);
    }

    private sealed class FirstExistenceReadTimeout(string table) : DbCommandInterceptor
    {
        public List<Guid?> TransactionIds { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal) &&
                command.CommandText.Contains($"FROM {table}", StringComparison.Ordinal))
            {
                TransactionIds.Add(eventData.Context?.Database.CurrentTransaction?.TransactionId);
                if (TransactionIds.Count == 1)
                    throw new TimeoutException("Injected timeout before the existence read.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class SavingAmbientObserver : SaveChangesInterceptor
    {
        public bool? AmbientDuringSave { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            AmbientDuringSave = Transaction.Current is not null;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FirstCommitAcknowledgmentTimeout : DbTransactionInterceptor
    {
        public int Commits { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Commits++;
            return Commits == 1
                ? Task.FromException(new TimeoutException("Injected timeout after the database committed."))
                : Task.CompletedTask;
        }
    }

    private static string NewId() => $"id_{Guid.NewGuid():N}";
    private static TrackedSaveRow NewRow(string id, string payload) => new() { Id = id, Payload = payload };
    private static TransactionScope BeginAmbientTransaction(IsolationLevel isolation) =>
        new(TransactionScopeOption.RequiresNew, new TransactionOptions { IsolationLevel = isolation }, TransactionScopeAsyncFlowOption.Enabled);

    private static async Task<Exception?> FailureAsync(Task operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}

public sealed class PostgreSqlTrackedSaveFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlTestDatabase _database = new();
    private readonly DefaultTenantAccessor _tenantAccessor = new();
    private ServiceProvider _services = null!;
    private string _observerConnection = null!;
    private string _writerConnection = null!;
    private IDbContextFactory<TrackedSaveContext> _factory = null!;

    public string Table => $"\"{TrackedSaveContext.SchemaName}\".\"{TrackedSaveContext.TableName}\"";
    public Store<TrackedSaveContext, TrackedSaveRow> Store { get; private set; } = null!;
    public Store<TrackedSaveContext, MappedBulkRow> MappedStore { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _database.OpenAsync();
        _writerConnection = new NpgsqlConnectionStringBuilder(_database.ConnectionString)
        {
            Pooling = true,
            MaxPoolSize = 16
        }.ConnectionString;
        _observerConnection = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Enlist = false }.ConnectionString;
        _services = new ServiceCollection()
            .AddSingleton<ITenantAccessor>(_tenantAccessor)
            .Configure<TenantsOptions>(options => options.IsEnabled = true)
            .AddScoped<IEntityModelCreatingHandler, SetTenantIdFilter>()
            .AddScoped<IEntitySavingHandler, ApplyTenantId>()
            .AddScoped<IEntitySavingHandler, RecordTrackedSaveHook>()
            .AddDbContextFactory<TrackedSaveContext>((_, builder) => builder.UseNpgsql(_writerConnection))
            .AddScoped<Store<TrackedSaveContext, TrackedSaveRow>>()
            .AddScoped<Store<TrackedSaveContext, MappedBulkRow>>()
            .BuildServiceProvider();
        _factory = _services.GetRequiredService<IDbContextFactory<TrackedSaveContext>>();
        Store = _services.GetRequiredService<Store<TrackedSaveContext, TrackedSaveRow>>();
        MappedStore = _services.GetRequiredService<Store<TrackedSaveContext, MappedBulkRow>>();
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public IDisposable UseTenant(string tenantId) => _tenantAccessor.PushContext(new Tenant { Id = tenantId, Name = tenantId });

    public HeldWrite SaveInHeldTransaction(Func<TaskCompletionSource<int>, Task> startSave, CancellationToken token) => new(startSave, token);

    public Task SaveWithShadowAsync(TrackedSaveRow row, string shadow, CancellationToken token, TaskCompletionSource<int>? backend = null) =>
        Store.SaveAsync(row, entity => entity.Id, async (context, entity, ct) =>
        {
            context.Entry(entity).Property("ShadowText").CurrentValue = shadow;
            if (backend != null)
                await ReadBackendAsync(context, backend, ct);
        }, token);

    public async Task SaveBulkAndReadBackendAsync(TrackedSaveRow row, TaskCompletionSource<int> backend, CancellationToken token)
    {
        await Store.SaveManyAsync([row], entity => entity.Id, (context, _, ct) => ReadBackendAsync(context, backend, ct), token);
    }

    public async ValueTask ReadBackendAsync(TrackedSaveContext context, TaskCompletionSource<int> backend, CancellationToken token)
    {
        await context.Database.OpenConnectionAsync(token);
        backend.SetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
    }

    public async Task<NpgsqlConnection> OpenObserverAsync(CancellationToken token)
    {
        var connection = new NpgsqlConnection(_observerConnection);
        try
        {
            await connection.OpenAsync(token);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task WaitForBlockedWriter(int blockerPid, string waitEvent, CancellationToken token)
    {
        await using var observer = await OpenObserverAsync(token);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_stat_activity a
                WHERE a.datname = current_database()
                  AND a.wait_event = $1
                  AND $2 = ANY (pg_blocking_pids(a.pid))
            )
            """, observer);
        command.Parameters.Add(new NpgsqlParameter { Value = waitEvent });
        command.Parameters.Add(new NpgsqlParameter { Value = blockerPid });

        while (!((bool)(await command.ExecuteScalarAsync(token))!))
            await Task.Delay(10, token);
    }

    public async Task<(string? Owner, string Payload, string? Shadow, string? Hook)> ReadAsync(string id, CancellationToken token)
    {
        await using var context = await _factory.CreateDbContextAsync(token);
        var row = await context.Set<TrackedSaveRow>().IgnoreQueryFilters().Where(row => row.Id == id)
            .Select(row => new { row.TenantId, row.Payload, Shadow = EF.Property<string?>(row, "ShadowText"), row.HookMarker })
            .SingleAsync(token);
        return (row.TenantId, row.Payload, row.Shadow, row.HookMarker);
    }

    public async Task<int> CountAsync(string[] ids, string payload, CancellationToken token)
    {
        await using var context = await _factory.CreateDbContextAsync(token);
        return await context.Set<TrackedSaveRow>().IgnoreQueryFilters()
            .CountAsync(row => ids.Contains(row.Id) && row.Payload == payload, token);
    }

    public async Task<string?> ReadScalarAsync(string query, string value, CancellationToken token)
    {
        await using var db = await OpenObserverAsync(token);
        await using var command = new NpgsqlCommand(query, db);
        command.Parameters.Add(new NpgsqlParameter { Value = value });
        return (string?)await command.ExecuteScalarAsync(token);
    }

    public async Task<int> CountAdvisoryLocksAsync(int backend, CancellationToken token)
    {
        await using var db = await OpenObserverAsync(token);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)::integer FROM pg_locks WHERE pid = $1 AND locktype = 'advisory'
            """, db);
        command.Parameters.Add(new NpgsqlParameter { Value = backend });
        return (int)(await command.ExecuteScalarAsync(token))!;
    }

    public NpgsqlConnection CreateOptedOutConnection() => new($"{_writerConnection};Enlist = false;Persist Security Info=false");

    public NpgsqlDataSource CreateOptedOutDataSource() => NpgsqlDataSource.Create($"{_writerConnection};Enlist = false;Persist Security Info=false");

    public (IDbContextFactory<TrackedSaveContext> Factory, Store<TrackedSaveContext, TrackedSaveRow> Store) CreateStore(
        string? enlistment = null, bool retry = false, IInterceptor? interceptor = null)
    {
        var connection = enlistment is null ? _writerConnection : $"{_writerConnection};{enlistment}";
        return CreateStore(options => options.UseNpgsql(connection, provider =>
        {
            if (retry)
                provider.EnableRetryOnFailure(2, TimeSpan.FromMilliseconds(5), null);
        }), interceptor);
    }

    public (IDbContextFactory<TrackedSaveContext> Factory, Store<TrackedSaveContext, TrackedSaveRow> Store) CreateStore(
        Action<DbContextOptionsBuilder<TrackedSaveContext>> configure, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<TrackedSaveContext>();
        configure(options);
        if (interceptor != null)
            options.AddInterceptors(interceptor);
        var factory = new TrackedSaveContextFactory(options.Options, _services);
        return (factory, new Store<TrackedSaveContext, TrackedSaveRow>(factory, _services));
    }

    private sealed class TrackedSaveContextFactory(DbContextOptions<TrackedSaveContext> options, IServiceProvider services)
        : IDbContextFactory<TrackedSaveContext>
    {
        public TrackedSaveContext CreateDbContext() => new(options, services);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_services != null)
                await _services.DisposeAsync();
        }
        finally
        {
            if (_writerConnection != null)
            {
                await using var writer = new NpgsqlConnection(_writerConnection);
                NpgsqlConnection.ClearPool(writer);
            }
            await _database.DisposeAsync();
        }
    }

    public sealed class HeldWrite : IAsyncDisposable
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _transaction;
        public Task<int> Ready { get; }

        public HeldWrite(Func<TaskCompletionSource<int>, Task> run, CancellationToken token)
        {
            var backend = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Ready = ready.Task;
            _transaction = Task.Run(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    using var scope = new TransactionScope(TransactionScopeOption.RequiresNew,
                        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);
                    await run(backend);
                    ready.SetResult(await backend.Task.WaitAsync(token));
                    await _release.Task.WaitAsync(token);
                    scope.Complete();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    ready.TrySetCanceled(token);
                    throw;
                }
                catch (Exception exception)
                {
                    ready.TrySetException(exception);
                    throw;
                }
            });
        }

        public Task ReleaseAsync()
        {
            _release.TrySetResult();
            return _transaction;
        }

        public async ValueTask DisposeAsync() => await ReleaseAsync();
    }
}

public sealed class TrackedSaveContext(DbContextOptions<TrackedSaveContext> options, IServiceProvider services)
    : FlammeDbContextBase(options, services)
{
    internal const string SchemaName = "Elsa";
    internal const string TableName = "TrackedSaveRows";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<TrackedSaveRow>().ToTable(TableName, SchemaName);
        row.HasKey(entity => entity.Id);
        row.Property(entity => entity.Id).HasConversion(v => v.ToUpperInvariant(), v => v.ToLowerInvariant());
        row.Property(entity => entity.Payload).HasConversion(v => v.ToUpperInvariant(), v => v.ToLowerInvariant());
        row.Property<string>("ShadowText");

        var mapped = modelBuilder.Entity<MappedBulkRow>().ToTable("MappedBulkRows", SchemaName);
        mapped.HasKey(entity => entity.Id);
        mapped.Property(entity => entity.Id).HasConversion(
            value => Encoding.UTF8.GetBytes(value.ToUpperInvariant()),
            value => Encoding.UTF8.GetString(value).ToLowerInvariant());
        mapped.Property(entity => entity.TenantId).HasConversion(value => "t:" + value, value => value.Substring(2));
        mapped.HasIndex(entity => entity.UniqueValue).IsUnique();

        base.OnModelCreating(modelBuilder);
    }
}

public sealed class TrackedSaveRow : Entity
{
    public string Payload { get; set; } = null!;
    public string? HookMarker { get; set; }
}

public sealed class MappedBulkRow : Entity
{
    public string UniqueValue { get; set; } = null!;
}

public sealed class RecordTrackedSaveHook : IEntitySavingHandler
{
    public ValueTask HandleAsync(FlammeDbContextBase dbContext, EntityEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Entity is TrackedSaveRow row)
            row.HookMarker = $"saved:{row.Payload}";
        return ValueTask.CompletedTask;
    }
}
