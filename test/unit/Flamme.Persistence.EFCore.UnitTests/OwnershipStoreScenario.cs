using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Flamme.Persistence.EFCore;
using Flamme.Tenants.Options;
using Flamme.Testing.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flamme.Persistence.EFCore.UnitTests;

public sealed class OwnershipStoreScenario : IAsyncDisposable
{
    private readonly Func<ValueTask> _disposeAsync;
    private readonly IDbContextFactory<OwnershipDbContext> _dbContextFactory;

    private OwnershipStoreScenario(
        TestTenantAccessor tenantAccessor,
        Store<OwnershipDbContext, OwnedRow> store,
        IDbContextFactory<OwnershipDbContext> dbContextFactory,
        Func<ValueTask> disposeAsync)
    {
        TenantAccessor = tenantAccessor;
        Store = store;
        _dbContextFactory = dbContextFactory;
        _disposeAsync = disposeAsync;
    }

    public TestTenantAccessor TenantAccessor { get; }
    public Store<OwnershipDbContext, OwnedRow> Store { get; }

    public IDisposable UseTenant(string tenantId) =>
        TenantAccessor.PushContext(tenantId == Tenant.DefaultTenantId
            ? Tenant.Default
            : new Tenant { Id = tenantId, Name = tenantId });

    public async Task<OwnedRow?> FindAsync(string id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.Rows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task ClearTenantIdAsync(string id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        await dbContext.Rows
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TenantId, (string?)null));
    }

    public ValueTask DisposeAsync() => _disposeAsync();

    public static async Task<OwnershipStoreScenario> CreatePostgreSqlAsync(string connectionString, string tenantId, bool tenantsEnabled)
    {
        var (isolatedConnectionString, adminConnectionString, database) = await CreateIsolatedDatabaseAsync(connectionString);
        try
        {
            return await CreateAsync(
                tenantId,
                tenantsEnabled,
                builder => builder.UseNpgsql(isolatedConnectionString),
                async () => await DropDatabaseAsync(adminConnectionString, database));
        }
        catch
        {
            await DropDatabaseAsync(adminConnectionString, database);
            throw;
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    public static async Task<OwnershipStoreScenario> CreateAsync(
        string tenantId,
        bool tenantsEnabled,
        Action<DbContextOptionsBuilder> configure,
        Func<ValueTask> disposeAsync)
    {
        var tenantAccessor = new TestTenantAccessor(tenantId);
        var services = new ServiceCollection()
            .AddSingleton<ITenantAccessor>(tenantAccessor)
            .Configure<TenantsOptions>(options => options.IsEnabled = tenantsEnabled)
            .AddDbContextFactory<OwnershipDbContext>((_, builder) => configure(builder))
            .AddSingleton(sp => new Store<OwnershipDbContext, OwnedRow>(
                sp.GetRequiredService<IDbContextFactory<OwnershipDbContext>>(),
                sp))
            .BuildServiceProvider();

        var factory = services.GetRequiredService<IDbContextFactory<OwnershipDbContext>>();
        await using (var dbContext = await factory.CreateDbContextAsync())
        {
            await dbContext.Database.EnsureCreatedAsync();

        }

        return new OwnershipStoreScenario(
            tenantAccessor,
            services.GetRequiredService<Store<OwnershipDbContext, OwnedRow>>(),
            factory,
            async () =>
            {
                await services.DisposeAsync();
                await disposeAsync();
            });
    }

    private static async Task<(string IsolatedConnectionString, string AdminConnectionString, string Database)> CreateIsolatedDatabaseAsync(string connectionString)
    {
        var database = $"elsa_savemany_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""CREATE DATABASE "{database}" """;
            await command.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database
        };
        return (target.ConnectionString, admin.ConnectionString, database);
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string database)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText =
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @database AND pid <> pg_backend_pid();
                """;
            terminate.Parameters.AddWithValue("database", database);
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"""DROP DATABASE IF EXISTS "{database}" """;
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class OwnershipDbContext : DbContext
{
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "This JIT-run test context exercises EF model discovery; production AOT support is tracked separately.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "This JIT-run test context exercises EF model discovery; production AOT support is tracked separately.")]
    public OwnershipDbContext(DbContextOptions<OwnershipDbContext> options) : base(options) { }

    public DbSet<OwnedRow> Rows => Set<OwnedRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("Elsa");
        modelBuilder.Entity<OwnedRow>().ToTable("OwnedRows", "Elsa");
        modelBuilder.Entity<OwnedRow>().HasKey(x => x.Id);
        modelBuilder.Entity<OwnedRow>().Property(x => x.Payload).IsRequired();
    }
}

public sealed class OwnedRow : Entity
{
    public string Payload { get; set; } = "";
}
