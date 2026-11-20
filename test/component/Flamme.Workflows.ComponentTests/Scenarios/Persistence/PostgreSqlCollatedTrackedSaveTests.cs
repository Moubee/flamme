using System.Diagnostics.CodeAnalysis;
using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.EntityHandlers;
using Flamme.Tenants.Options;
using Flamme.Testing.Shared.Databases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flamme.Workflows.ComponentTests.Scenarios.Persistence;

public sealed class PostgreSqlCollatedTrackedSaveTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task EquivalentFirstSavesUseTheSameLock(bool citext, bool requiredPayloadDomain)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        await using var database = new PostgreSqlTestDatabase();
        await database.OpenAsync();
        var observer = database.Connection;
        await using (var setup = observer.CreateCommand())
        {
            setup.CommandText = """
                CREATE SCHEMA "Lock {schema}";
                CREATE COLLATION "Lock {schema}"."Case Insensitive"
                    (provider = icu, locale = 'und-u-ks-level2', deterministic = false);
                CREATE EXTENSION citext;

                CREATE TABLE "Lock {schema}"."Rows" (
                    "Physical {key}" text COLLATE "Lock {schema}"."Case Insensitive" PRIMARY KEY,
                    "Payload" text NOT NULL, "HookMarker" text, "ShadowText" text, unused json);
                CREATE TABLE "Lock {schema}"."Citext Rows" (
                    "Physical {key}" citext PRIMARY KEY,
                    "Payload" text NOT NULL, "HookMarker" text, "ShadowText" text, unused json);
                """;
            await setup.ExecuteNonQueryAsync(token);

            if (requiredPayloadDomain)
            {
                setup.CommandText = """
                    CREATE DOMAIN "Lock {schema}"."Required Payload" AS text NOT NULL;
                    ALTER TABLE "Lock {schema}"."Rows"
                        ALTER COLUMN "Payload" TYPE "Lock {schema}"."Required Payload";
                    """;
                await setup.ExecuteNonQueryAsync(token);
            }
        }

        await observer.ReloadTypesAsync(token);

        var hook = new CollatedSaveHook();
        var registrations = new ServiceCollection()
            .Configure<TenantsOptions>(options => options.IsEnabled = false)
            .AddSingleton<IEntitySavingHandler>(hook)
            .AddDbContextFactory<CollatedTextSaveContext>(builder => builder.UseNpgsql(database.ConnectionString))
            .AddDbContextFactory<CitextSaveContext>(builder => builder.UseNpgsql(database.ConnectionString))
            .AddDbContextFactory<UnmappedCitextSaveContext>(builder => builder.UseNpgsql(database.ConnectionString));
        await using var services = registrations.BuildServiceProvider();

        if (citext)
            await VerifyAsync<CitextSaveContext>(services, "Citext Rows", observer, hook, token, verifyUnmappedCitext: true);
        else
            await VerifyAsync<CollatedTextSaveContext>(services, "Rows", observer, hook, token);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SameOwnerCanSaveShortFixedWidthKeyAgain(bool mapped)
    {
        async Task VerifyAsync<TContext>(Store<TContext, CollatedSaveRow> store, NpgsqlConnection observer,
            DefaultTenantAccessor tenants, CancellationToken token) where TContext : FlammeDbContextBase
        {
            using var owner = tenants.PushContext(new Tenant { Id = "owner", Name = "Owner" });
            await store.SaveAsync(new CollatedSaveRow { Id = "A", Payload = "first" }, row => row.Id, token);
            await store.SaveAsync(new CollatedSaveRow { Id = "A", Payload = "second" }, row => row.Id, token);

            await using var command = observer.CreateCommand();
            command.CommandText = """SELECT "Id", "TenantId", "Payload" FROM "Tenant Key Rows" """;
            await using var reader = await command.ExecuteReaderAsync(token);
            await Assert.That(await reader.ReadAsync(token)).IsTrue();
            await Assert.That(reader.GetString(0)).IsEqualTo("A   ");
            await Assert.That(reader.GetString(1)).IsEqualTo("owner");
            await Assert.That(reader.GetString(2)).IsEqualTo("second");
            await Assert.That(await reader.ReadAsync(token)).IsFalse();
        }

        if (mapped)
            await WithTenantKeysAsync<MappedFixedWidthSaveContext>("character(4)", VerifyAsync);
        else
            await WithTenantKeysAsync<TenantTextSaveContext>("character(4)", VerifyAsync);
    }

    [Test]
    [Arguments("text")]
    [Arguments("icu")]
    [Arguments("citext")]
    public async Task TenantSaveKeepsNonFixedWidthSpelling(string kind)
    {
        if (kind == "citext")
            await WithTenantKeysAsync<TenantCitextSaveContext>(kind, VerifyAsync);
        else
            await WithTenantKeysAsync<TenantTextSaveContext>(kind, VerifyAsync);

        async Task VerifyAsync<TContext>(Store<TContext, CollatedSaveRow> store, NpgsqlConnection observer,
            DefaultTenantAccessor tenants, CancellationToken token) where TContext : FlammeDbContextBase
        {
            using var owner = tenants.PushContext(new Tenant { Id = "owner", Name = "Owner" });
            await store.SaveAsync(new CollatedSaveRow { Id = "A", Payload = "first" }, row => row.Id, token);
            var alias = new CollatedSaveRow { Id = kind == "citext" ? "a" : "A ", Payload = "second" };
            if (kind == "text")
                await store.SaveAsync(alias, row => row.Id, token);
            else
                await Assert.That(() => store.SaveAsync(alias, row => row.Id, token)).Throws<InvalidOperationException>();

            await using var command = observer.CreateCommand();
            command.CommandText = """SELECT "Id", "Payload" FROM "Tenant Key Rows" ORDER BY "Id" COLLATE "C" """;
            await using var rows = await command.ExecuteReaderAsync(token);
            await Assert.That(await rows.ReadAsync(token)).IsTrue();
            await Assert.That(rows.GetString(0)).IsEqualTo("A");
            await Assert.That(rows.GetString(1)).IsEqualTo("first");
            if (kind == "text")
            {
                await Assert.That(await rows.ReadAsync(token)).IsTrue();
                await Assert.That(rows.GetString(0)).IsEqualTo("A ");
                await Assert.That(rows.GetString(1)).IsEqualTo("second");
            }

            await Assert.That(await rows.ReadAsync(token)).IsFalse();
        }
    }

    [Test]
    public Task AnotherTenantCannotReplacePaddedKey() =>
        WithTenantKeysAsync<TenantTextSaveContext>("character(4)", async (store, observer, tenants, token) =>
        {
            using (tenants.PushContext(new Tenant { Id = "owner", Name = "Owner" }))
                await store.SaveAsync(new CollatedSaveRow { Id = "A", Payload = "first" }, row => row.Id, token);

            using (tenants.PushContext(new Tenant { Id = "other", Name = "Other" }))
                await Assert.That(() => store.SaveAsync(new CollatedSaveRow { Id = "A", Payload = "other" }, row => row.Id, token))
                    .Throws<InvalidOperationException>();

            await using var command = observer.CreateCommand();
            command.CommandText = """SELECT "TenantId", "Payload" FROM "Tenant Key Rows" """;
            await using var rows = await command.ExecuteReaderAsync(token);
            await Assert.That(await rows.ReadAsync(token)).IsTrue();
            await Assert.That(rows.GetString(0)).IsEqualTo("owner");
            await Assert.That(rows.GetString(1)).IsEqualTo("first");
            await Assert.That(await rows.ReadAsync(token)).IsFalse();
        });

    private static async Task WithTenantKeysAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                    DynamicallyAccessedMemberTypes.NonPublicConstructors |
                                    DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        string kind, Func<Store<TContext, CollatedSaveRow>, NpgsqlConnection, DefaultTenantAccessor, CancellationToken, Task> verify)
        where TContext : FlammeDbContextBase
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        await using var database = new PostgreSqlTestDatabase();
        await database.OpenAsync();
        var observer = database.Connection;

        var columnType = kind switch
        {
            "icu" => "text COLLATE \"Ignore Space\"",
            "citext" => "citext",
            "text" => "text",
            "character(4)" => "character(4)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await using (var setup = observer.CreateCommand())
        {
            setup.CommandText = $"""
                CREATE COLLATION "Ignore Space"
                    (provider = icu, locale = 'und-u-ka-shifted-ks-level2', deterministic = false);
                CREATE EXTENSION citext;
                CREATE TABLE "Tenant Key Rows" (
                    "Id" {columnType} PRIMARY KEY, "TenantId" text, "Payload" text NOT NULL, "HookMarker" text);
                """;
            await setup.ExecuteNonQueryAsync(token);
        }

        await observer.ReloadTypesAsync(token);
        var tenants = new DefaultTenantAccessor();
        // Leave the model unfiltered to exercise the Store's ownership guard directly.
        await using var services = new ServiceCollection()
            .AddSingleton<ITenantAccessor>(tenants)
            .Configure<TenantsOptions>(options => options.IsEnabled = true)
            .AddScoped<IEntitySavingHandler, ApplyTenantId>()
            .AddDbContextFactory<TContext>(builder => builder.UseNpgsql(database.ConnectionString))
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IDbContextFactory<TContext>>();
        await verify(new Store<TContext, CollatedSaveRow>(factory, services), observer, tenants, token);
    }

    private static async Task VerifyAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                    DynamicallyAccessedMemberTypes.NonPublicConstructors |
                                    DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        IServiceProvider services, string table, NpgsqlConnection observer, CollatedSaveHook hook, CancellationToken token,
        bool verifyUnmappedCitext = false)
        where TContext : FlammeDbContextBase
    {
        var factory = services.GetRequiredService<IDbContextFactory<TContext>>();
        var store = new Store<TContext, CollatedSaveRow>(factory, services);
        var firstContext = new TaskCompletionSource<DbContextId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondContext = new TaskCompletionSource<DbContextId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Save("abc", "first", "shadow first", firstContext);
        Task? second = null;
        var sameKeyWaited = false;
        var unmappedKeyWaited = true;
        try
        {
            var owner = await hook.FirstAtSaving.WaitAsync(token);
            if (verifyUnmappedCitext)
                unmappedKeyWaited = await ProbeUnmappedCitextLockAsync(services, observer, owner, hook, token);

            second = Save("ABC", "second", "shadow second", secondContext);
            sameKeyWaited = await WaitForAdvisoryLockAsync(observer, owner, hook.SecondAtSaving, second, token);

            // The first saving hook is still held, with its row absent. A distinct key can finish.
            await Save("def", "cold", "shadow cold").WaitAsync(token);
            await using var cold = observer.CreateCommand();
            cold.CommandText = $$"""SELECT "HookMarker" FROM "Lock {schema}"."{{table}}" WHERE "Physical {key}" = 'def'""";
            await Assert.That(await cold.ExecuteScalarAsync(token)).IsEqualTo("saved:cold");
        }
        finally
        {
            hook.ReleaseFirst();
            await Task.WhenAll(first, second ?? Task.CompletedTask);
        }

        await Assert.That(sameKeyWaited).IsTrue();
        await Assert.That(unmappedKeyWaited).IsTrue();
        await Assert.That(await firstContext.Task).IsNotEqualTo(await secondContext.Task);
        await using var stored = observer.CreateCommand();
        stored.CommandText = $$"""
            SELECT "Physical {key}", "Payload", "HookMarker", "ShadowText"
            FROM "Lock {schema}"."{{table}}" WHERE "Physical {key}" = 'abc'
            """;
        await using var rows = await stored.ExecuteReaderAsync(token);
        await Assert.That(await rows.ReadAsync(token)).IsTrue();
        await Assert.That(rows.GetString(0)).IsEqualTo("abc");
        await Assert.That(rows.GetString(1)).IsEqualTo("SECOND");
        await Assert.That(rows.GetString(2)).IsEqualTo("saved:second");
        await Assert.That(rows.GetString(3)).IsEqualTo("shadow second");
        await Assert.That(await rows.ReadAsync(token)).IsFalse();

        Task Save(string id, string payload, string shadow, TaskCompletionSource<DbContextId>? contextId = null) =>
            store.SaveAsync(new CollatedSaveRow { Id = id, Payload = payload }, x => x.Id, (context, row, _) =>
            {
                contextId?.SetResult(context.ContextId);
                context.Entry(row).Property("ShadowText").CurrentValue = shadow;
                return ValueTask.CompletedTask;
            }, token);
    }

    private static async Task<bool> ProbeUnmappedCitextLockAsync(
        IServiceProvider services, NpgsqlConnection observer, int owner, CollatedSaveHook hook, CancellationToken token)
    {
        // The physical key is citext even when the ordinary EF predicate binds text.
        // Cancel before the waiter can issue that predicate.
        var factory = services.GetRequiredService<IDbContextFactory<UnmappedCitextSaveContext>>();
        var store = new Store<UnmappedCitextSaveContext, CollatedSaveRow>(factory, services);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var waiting = store.SaveAsync(new CollatedSaveRow { Id = "ABC", Payload = "probe" }, x => x.Id, cancel.Token);
        try
        {
            return await WaitForAdvisoryLockAsync(observer, owner, hook.ProbeAtSaving, waiting, token);
        }
        finally
        {
            await cancel.CancelAsync();
            await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
        }
    }

    private static async Task<bool> WaitForAdvisoryLockAsync(
        NpgsqlConnection observer, int owner, Task atSaving, Task save, CancellationToken token)
    {
        await using var command = observer.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event = 'advisory'
                    AND $1 = ANY(pg_blocking_pids(pid)))
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = owner });
        while (!atSaving.IsCompleted && !save.IsCompleted)
        {
            if ((bool)(await command.ExecuteScalarAsync(token))!)
                return true;
            await Task.Delay(10, token);
        }

        return false;
    }
}

public abstract class CollatedSaveContext(DbContextOptions options, IServiceProvider services, string table, bool citext)
    : FlammeDbContextBase(options, services)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<CollatedSaveRow>().ToTable(table, "Lock {schema}");
        row.HasKey(x => x.Id);
        row.Ignore(x => x.TenantId);
        var key = row.Property(x => x.Id).HasColumnName("Physical {key}");
        if (citext)
            key.HasColumnType("citext");
        row.Property(x => x.Payload).HasConversion(x => x.ToUpperInvariant(), x => x.ToLowerInvariant());
        row.Property<string>("ShadowText");
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class CollatedTextSaveContext(DbContextOptions<CollatedTextSaveContext> options, IServiceProvider services)
    : CollatedSaveContext(options, services, "Rows", false);

public sealed class CitextSaveContext(DbContextOptions<CitextSaveContext> options, IServiceProvider services)
    : CollatedSaveContext(options, services, "Citext Rows", true);

public sealed class UnmappedCitextSaveContext(DbContextOptions<UnmappedCitextSaveContext> options, IServiceProvider services)
    : CollatedSaveContext(options, services, "Citext Rows", false);

public abstract class TenantKeySaveContext(DbContextOptions options, IServiceProvider services, string? columnType)
    : FlammeDbContextBase(options, services)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<CollatedSaveRow>().ToTable("Tenant Key Rows", "public");
        row.HasKey(x => x.Id);
        if (columnType is not null)
            row.Property(x => x.Id).HasColumnType(columnType);
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class TenantTextSaveContext(DbContextOptions<TenantTextSaveContext> options, IServiceProvider services)
    : TenantKeySaveContext(options, services, null);

public sealed class MappedFixedWidthSaveContext(DbContextOptions<MappedFixedWidthSaveContext> options, IServiceProvider services)
    : TenantKeySaveContext(options, services, "character(4)");

public sealed class TenantCitextSaveContext(DbContextOptions<TenantCitextSaveContext> options, IServiceProvider services)
    : TenantKeySaveContext(options, services, "citext");

public sealed class CollatedSaveRow : Entity
{
    public string Payload { get; set; } = null!;
    public string? HookMarker { get; set; }
}

public sealed class CollatedSaveHook : IEntitySavingHandler
{
    private readonly TaskCompletionSource<int> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _probe = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<int> FirstAtSaving => _first.Task;
    public Task SecondAtSaving => _second.Task;
    public Task ProbeAtSaving => _probe.Task;

    public void ReleaseFirst() => _release.TrySetResult();

    public async ValueTask HandleAsync(FlammeDbContextBase dbContext, EntityEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Entity is not CollatedSaveRow row)
            return;

        row.HookMarker = $"saved:{row.Payload}";
        if (row.Payload == "first")
        {
            _first.SetResult(((NpgsqlConnection)dbContext.Database.GetDbConnection()).ProcessID);
            await _release.Task.WaitAsync(cancellationToken);
        }
        else if (row.Payload == "second")
            _second.SetResult();
        else if (row.Payload == "probe")
        {
            _probe.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
