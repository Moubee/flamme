using System.Diagnostics;
using System.Transactions;
using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.EntityHandlers;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Tenants.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
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
        Console.WriteLine($"i11 native bulk first: winner={stored.Payload} tracked_sqlstate={sqlState} callbacks={callbacks}");
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
        Console.WriteLine("i11 tracked first: native bulk settled the ensuing conflict");
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

        var clock = Stopwatch.StartNew();
        var cold = Task.WhenAll(ids.Skip(1).Select(id => fixture.SaveWithShadowAsync(NewRow(id, "cold"), "cold", token)));
        await Task.WhenAny(cold, Task.Delay(TimeSpan.FromSeconds(1), token));
        var persisted = await fixture.CountAsync(ids[1..], "cold", token);
        var elapsed = clock.Elapsed.TotalMilliseconds;
        var hotBeforeRelease = hot.IsCompleted;

        await transaction.RollbackAsync(token);
        await Task.WhenAll(cold, hot);
        Console.WriteLine(FormattableString.Invariant($"i11 held checkpoint persisted={persisted}/48 before_release_ms={elapsed:F1}"));
        await Assert.That(hotBeforeRelease).IsFalse();
        await Assert.That(persisted >= 44).IsTrue();
        await Assert.That(await fixture.CountAsync(ids[1..], "cold", token)).IsEqualTo(48);
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
        Console.WriteLine($"i11 non-participating AddAsync winner preserved; tracked SQLSTATE={sqlState}");
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
        var (locks, maxLocks, maxConnections, maxPrepared) = await fixture.ReadLockBudgetAsync(heldPid, token);
        await Assert.That(locks).IsEqualTo(0);
        await holder.ReleaseAsync();
        await Assert.That(await fixture.CountAsync(rows.Select(row => row.Id).ToArray(), "bulk", token)).IsEqualTo(rows.Length);
        Console.WriteLine($"i11 native batch={rows.Length} insert_only={insertOnly} advisory_held={locks} max_locks_per_transaction={maxLocks} max_connections={maxConnections} max_prepared_transactions={maxPrepared}");
    }

    [Test]
    public async Task RetryingProviderAcceptsLocalWriteAndCallerWrappedAmbientTransaction()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        using var tenant = fixture.UseTenant("a");
        var (factory, store) = fixture.CreateRetryingStore();
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
        Console.WriteLine("i11 retry strategy: local success; caller-wrapped ambient success; unwrapped ambient rejected");
    }

    private static string NewId() => $"id_{Guid.NewGuid():N}";
    private static TrackedSaveRow NewRow(string id, string payload) => new() { Id = id, Payload = payload };

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
    private readonly string _schema = $"i11_{Guid.NewGuid():N}";
    private readonly string _app = $"i11-{Guid.NewGuid():N}";
    private readonly DefaultTenantAccessor _tenantAccessor = new();
    private ServiceProvider _services = null!;
    private PostgreSqlContainer? _container;
    private string _observerConnection = null!;
    private string _writerConnection = null!;
    private IDbContextFactory<TrackedSaveContext> _factory = null!;

    public string Table => $"\"{_schema}\".\"Rows\"";
    public Store<TrackedSaveContext, TrackedSaveRow> Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLAMME_I11_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _container.StartAsync();
            connectionString = _container.GetConnectionString();
        }

        var userConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = _app,
            MaxPoolSize = 16
        };
        _writerConnection = userConnection.ConnectionString;
        _observerConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = _app + "-observer",
            Enlist = false
        }.ConnectionString;
        _services = new ServiceCollection()
            .AddSingleton<ITenantAccessor>(_tenantAccessor)
            .Configure<TenantsOptions>(options => options.IsEnabled = true)
            .AddScoped<IEntityModelCreatingHandler, SetTenantIdFilter>()
            .AddScoped<IEntitySavingHandler, ApplyTenantId>()
            .AddScoped<IEntitySavingHandler, RecordTrackedSaveHook>()
            .AddDbContextFactory<TrackedSaveContext>((_, builder) => builder.UseNpgsql(userConnection.ConnectionString)
                .UseFlammeDbContextOptions(new FlammeDbContextOptions { SchemaName = _schema }))
            .AddScoped<Store<TrackedSaveContext, TrackedSaveRow>>()
            .BuildServiceProvider();
        _factory = _services.GetRequiredService<IDbContextFactory<TrackedSaveContext>>();
        Store = _services.GetRequiredService<Store<TrackedSaveContext, TrackedSaveRow>>();
        await using var db = await _factory.CreateDbContextAsync();
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public IDisposable UseTenant(string tenantId) => _tenantAccessor.PushContext(new Tenant { Id = tenantId, Name = tenantId });

    public HeldWrite SaveInHeldTransaction(Func<TaskCompletionSource<int>, Task> startSave, CancellationToken token) => new(startSave, token);

    public Task SaveWithShadowAsync(TrackedSaveRow row, string shadow, CancellationToken token, TaskCompletionSource<int>? backend = null) =>
        Store.SaveAsync(row, entity => entity.Id, async (context, entity, ct) =>
        {
            context.Entry(entity).Property("ShadowText").CurrentValue = shadow;
            if (backend != null)
            {
                await context.Database.OpenConnectionAsync(ct);
                backend.SetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
            }
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
                  AND a.application_name = $1
                  AND a.wait_event = $2
                  AND $3 = ANY (pg_blocking_pids(a.pid))
            )
            """, observer);
        command.Parameters.Add(new NpgsqlParameter { Value = _app });
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

    public async Task<(int Count, string MaxLocks, string MaxConnections, string MaxPrepared)> ReadLockBudgetAsync(
        int backend, CancellationToken token)
    {
        await using var db = await OpenObserverAsync(token);
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*)::integer FROM pg_locks WHERE pid = $1 AND locktype = 'advisory' AND granted),
                current_setting('max_locks_per_transaction'), current_setting('max_connections'),
                current_setting('max_prepared_transactions')
            """, db);
        command.Parameters.Add(new NpgsqlParameter { Value = backend });
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    public (IDbContextFactory<TrackedSaveContext>, Store<TrackedSaveContext, TrackedSaveRow>) CreateRetryingStore()
    {
        var options = new DbContextOptionsBuilder<TrackedSaveContext>();
        options.UseNpgsql(_writerConnection, provider => provider.EnableRetryOnFailure())
            .UseFlammeDbContextOptions(new FlammeDbContextOptions { SchemaName = _schema });
        var factory = new RetryContextFactory(options.Options, _services);
        return (factory, new Store<TrackedSaveContext, TrackedSaveRow>(factory, _services));
    }

    private sealed class RetryContextFactory(DbContextOptions<TrackedSaveContext> options, IServiceProvider services)
        : IDbContextFactory<TrackedSaveContext>
    {
        public TrackedSaveContext CreateDbContext() => new(options, services);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services != null)
        {
            await using var db = await OpenObserverAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", db);
            await command.ExecuteNonQueryAsync();
            await _services.DisposeAsync();
        }

        if (_container != null)
            await _container.DisposeAsync();
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
                    using var scope = new TransactionScope(TransactionScopeOption.RequiresNew,
                        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);
                    await run(backend);
                    ready.SetResult(await backend.Task.WaitAsync(token));
                    await _release.Task.WaitAsync(token);
                    scope.Complete();
                }
                catch (Exception exception)
                {
                    ready.TrySetException(exception);
                    throw;
                }
            }, token);
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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<TrackedSaveRow>().ToTable("Rows");
        row.HasKey(entity => entity.Id);
        row.Property(entity => entity.Id).HasConversion(v => v.ToUpperInvariant(), v => v.ToLowerInvariant());
        row.Property(entity => entity.Payload).HasConversion(v => v.ToUpperInvariant(), v => v.ToLowerInvariant());
        row.Property<string>("ShadowText");
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class TrackedSaveRow : Entity
{
    public string Payload { get; set; } = null!;
    public string? HookMarker { get; set; }
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
