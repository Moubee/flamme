using System.Diagnostics.CodeAnalysis;
using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.EntityHandlers;
using Flamme.Tenants.Options;
using Flamme.Testing.Shared.Databases;
using Flamme.Testing.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Persistence.ConformanceTests;

public class DetachedStoreUpdateTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("named")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The test creates its custom mapped table in a disposable PostgreSQL database.")]
    public async Task PartialUpdatesWriteOnlySelectedColumnsAndUseTheMappedKey(string? storedOwner)
    {
        await using var database = new PostgreSqlTestDatabase();
        await database.OpenAsync();
        var writer = storedOwner ?? Tenant.DefaultTenantId;
        var commands = new List<string>();
        await using var services = new ServiceCollection()
            .AddSingleton<ITenantAccessor>(new TestTenantAccessor(writer))
            .Configure<TenantsOptions>(options => options.IsEnabled = true)
            .AddScoped<IEntitySavingHandler, ApplyTenantId>()
            .AddScoped<IEntityModelCreatingHandler, SetTenantIdFilter>()
            .AddDbContextFactory<PatchContext>(options => options
                .UseNpgsql(database.ConnectionString)
                .LogTo(commands.Add, [RelationalEventId.CommandExecuted]))
            .AddScoped<Store<PatchContext, PatchRow>>()
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<Store<PatchContext, PatchRow>>();
        await using var reader = await store.CreateDbContextAsync();
        await reader.Database.EnsureCreatedAsync();
        await reader.Database.ExecuteSqlAsync($"""
            INSERT INTO public."PatchRows" ("Code", "Sequence", "TenantId", "Name") VALUES
                ('target', 1, {storedOwner}, 'initial'),
                ('target', 2, {storedOwner}, 'delete'),
                ('target', 3, {storedOwner}, 'full'),
                ('target', 4, 'other', 'foreign'),
                ('target', 5, NULL, 'other sequence')
            """);

        var patch = new PatchRow { Code = "target", Sequence = 1, Name = "changed" };
        commands.Clear();
        await store.UpdatePartialAsync(patch, []);
        await Assert.That(commands).IsEmpty();

        await store.UpdatePartialAsync(patch, [x => x.Name]);
        var update = commands.Single(command => command.Contains("UPDATE ", StringComparison.Ordinal));
        var assignments = update[update.IndexOf("UPDATE ", StringComparison.Ordinal)..update.IndexOf("WHERE ", StringComparison.Ordinal)];
        await Assert.That(assignments).DoesNotContain("\"TenantId\"");
        await Assert.That(update).Contains("\"TenantId\"");
        var reloaded = await reader.Rows.AsNoTracking().SingleAsync(row => row.Sequence == 1);
        await Assert.That(reloaded.TenantId).IsEqualTo(storedOwner);
        await Assert.That(reloaded.Name).IsEqualTo("changed");

        var tracked = await reader.Rows.SingleAsync(row => row.Sequence == 1);
        tracked.Name = "tracked change";
        await reader.SaveChangesAsync();
        await reader.Entry(tracked).ReloadAsync();
        await Assert.That(tracked.TenantId).IsEqualTo(storedOwner);

        await store.UpdatePartialAsync(patch, [x => x.TenantId!]);
        await Assert.That(await reader.Rows.Where(row => row.Sequence == 1).Select(row => row.TenantId).SingleAsync()).IsEqualTo(writer);
        await store.UpdateAsync(new PatchRow { Code = "target", Sequence = 3, Name = "full update" });
        var full = await reader.Rows.AsNoTracking().SingleAsync(row => row.Sequence == 3);
        await Assert.That(full.Name).IsEqualTo("full update");
        await Assert.That(full.TenantId).IsEqualTo(writer);
        await Assert.That(await store.DeleteAsync(new PatchRow { Code = "target", Sequence = 2 })).IsTrue();
        await Assert.That(await reader.Rows.AnyAsync(row => row.Sequence == 2)).IsFalse();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            store.UpdatePartialAsync(new PatchRow { Code = "target", Sequence = 4, Name = "changed" }, [x => x.Name]));
        var foreign = await reader.Rows.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Sequence == 4);
        await Assert.That(foreign.Name).IsEqualTo("foreign");
        await Assert.That(foreign.TenantId).IsEqualTo("other");

        await store.AddAsync(new PatchRow { Code = "new", Sequence = 6 });
        await Assert.That(await reader.Rows.Where(row => row.Code == "new").Select(row => row.TenantId).SingleAsync()).IsEqualTo(writer);
    }

    private sealed class PatchRow : Entity
    {
        public string Code { get; init; } = "";
        public int Sequence { get; init; }
        public string Name { get; set; } = "";
    }

    private sealed class PatchContext(DbContextOptions<PatchContext> options, IServiceProvider services)
        : FlammeDbContextBase(options, services)
    {
        public DbSet<PatchRow> Rows => Set<PatchRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<PatchRow>();
            row.ToTable("PatchRows", "public");
            row.Ignore(x => x.Id);
            row.HasKey(x => new { x.Code, x.Sequence });
            base.OnModelCreating(modelBuilder);
        }
    }
}
