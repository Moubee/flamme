using System.Diagnostics.CodeAnalysis;
using Flamme.Common.Multitenancy;
using Flamme.Extensions;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Tenants.Extensions;
using Flamme.Testing.Shared;
using Flamme.Testing.Shared.Databases;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Mappers;
using Flamme.Workflows.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flamme.Workflows.IntegrationTests;

[NotInParallel(nameof(PostgreSqlWorkflowInstanceWriteTests))]
public sealed class PostgreSqlWorkflowInstanceWriteTests
{
    private readonly PostgreSqlTestDatabase _database = new();
    private ServiceProvider _services = null!;
    private IWorkflowInstanceStore Instances => _services.GetRequiredService<IWorkflowInstanceStore>();
    private EntityStore<ManagementFlammeDbContext, WorkflowInstance> Rows =>
        _services.GetRequiredService<EntityStore<ManagementFlammeDbContext, WorkflowInstance>>();

    [Before(Test)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This integration test migrates a real PostgreSQL database.")]
    public async Task InitializeAsync()
    {
        await _database.OpenAsync();
        _services = (ServiceProvider)new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .ConfigureFlamme(flamme => flamme.UseTenants().UseWorkflowManagement(management =>
                management.UseWorkflowInstances(instances => instances.UseEntityFrameworkCore(ef => ef.UsePostgreSql(_database.ConnectionString)))))
            .Build();
        await using var db = await Rows.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    [After(Test)]
    public async Task DisposeAsync()
    {
        if (_services != null)
            await _services.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Test]
    public async Task UnrelatedInstancesCommitWhileAnExistingInstanceWaits()
    {
        string[] free = ["free-a", "free-b"];
        await Instances.SaveManyAsync(free.Append("held").Select(id => Instance(id, "seed")));
        await using var tx = await _database.Connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""SELECT "Id" FROM "Elsa"."WorkflowInstances" WHERE "Id" = 'held' FOR UPDATE""", _database.Connection, tx);
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo("held");
        var held = Instances.SaveAsync(Instance("held", "updated")).AsTask();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await WaitForBlockedWritersAsync(1, deadline.Token);
            await Task.WhenAll(free.Select(id => Instances.SaveAsync(Instance(id, "updated"), deadline.Token).AsTask()));
            foreach (var id in free)
                await AssertStoredAsync(id, "updated");
            await Assert.That(held.IsCompleted).IsFalse();
        }
        finally
        {
            await tx.RollbackAsync();
            await held;
        }

        await AssertStoredAsync("held", "updated");
    }

    [Test]
    public async Task ConcurrentFirstSavesRunEachCallbackOnceAndKeepOneWholeInstance()
    {
        await using var tx = await _database.Connection.BeginTransactionAsync();
        // Both existence reads can pass SHARE. Each INSERT must then wait for release.
        await using var command = new NpgsqlCommand("""LOCK TABLE "Elsa"."WorkflowInstances" IN SHARE MODE""", _database.Connection, tx);
        await command.ExecuteNonQueryAsync();
        var callbacks = new int[2];
        var saves = new[] { AttemptAsync(0), AttemptAsync(1) };
        string?[] results;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await WaitForBlockedWritersAsync(2, deadline.Token);
        }
        finally
        {
            await tx.RollbackAsync();
            results = await Task.WhenAll(saves);
        }

        var winner = results.Single(marker => marker != null)!;
        await Assert.That(callbacks.SequenceEqual([1, 1])).IsTrue();
        await using var reader = await Rows.CreateDbContextAsync();
        await Assert.That(await reader.WorkflowInstances.CountAsync()).IsEqualTo(1);
        await AssertStoredAsync("same", winner);

        async Task<string?> AttemptAsync(int index)
        {
            var marker = $"caller-{index}";
            try
            {
                await Rows.SaveAsync(Instance("same", marker), (db, row, _) =>
                {
                    Interlocked.Increment(ref callbacks[index]);
                    SetSerializedState(db, row);
                    return ValueTask.CompletedTask;
                });
                return marker;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_WorkflowInstances" })
            {
                return null;
            }
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task BatchCallbacksFinishInOrderAndPersistPreparedState(bool insert)
    {
        string[] ids = ["c", "a", "b"];
        var rows = ids.Select(id => Instance(id, id)).ToArray();
        var completed = new List<string>();
        var active = 0;

        if (insert)
            await Rows.AddManyAsync(rows, Prepare);
        else
            await Rows.SaveManyAsync(rows, Prepare);

        await Assert.That(completed.SequenceEqual(ids)).IsTrue();
        foreach (var id in ids)
            await AssertStoredAsync(id, "prepared-" + id);

        async ValueTask Prepare(ManagementFlammeDbContext db, WorkflowInstance row, CancellationToken ct)
        {
            if (Interlocked.Increment(ref active) != 1)
                throw new InvalidOperationException("Two callbacks overlapped on the batch DbContext.");
            await db.WorkflowInstances.AnyAsync(ct);
            row.Name = row.WorkflowState.Name = "prepared-" + row.Id;
            SetSerializedState(db, row);
            completed.Add(row.Id);
            Interlocked.Decrement(ref active);
        }
    }

    private WorkflowInstance Instance(string id, string name) =>
        _services.GetRequiredService<WorkflowStateMapper>().Map(new WorkflowState
        {
            Id = id, Name = name, DefinitionId = "workflow", DefinitionVersionId = "v1", DefinitionVersion = 1
        })!;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The callback exercises the same workflow state serializer as the instance store.")]
    private void SetSerializedState(ManagementFlammeDbContext db, WorkflowInstance row) =>
        db.Entry(row).Property("Data").CurrentValue = _services.GetRequiredService<IWorkflowStateSerializer>().Serialize(row.WorkflowState);

    private async Task AssertStoredAsync(string id, string name)
    {
        var stored = await Assert.That(await Instances.FindAsync(new() { Id = id })).IsNotNull();
        await Assert.That(stored.TenantId).IsEqualTo(Tenant.DefaultTenantId);
        await Assert.That(stored.Name).IsEqualTo(name);
        await Assert.That(stored.WorkflowState.Name).IsEqualTo(name);
        await Assert.That(stored.WorkflowState.Id).IsEqualTo(id);
    }

    private async Task WaitForBlockedWritersAsync(int count, CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(_database.ConnectionString);
        await observer.OpenAsync(ct);
        await using var query = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock'
              AND $1 = ANY(pg_blocking_pids(pid))
            """, observer);
        query.Parameters.Add(new NpgsqlParameter { Value = _database.Connection.ProcessID });
        while ((long)(await query.ExecuteScalarAsync(ct))! != count)
            await Task.Delay(10, ct);
    }
}
