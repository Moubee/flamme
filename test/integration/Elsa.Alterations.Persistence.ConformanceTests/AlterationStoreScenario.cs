using System.Text.Json;
using Elsa.Alterations.Core.Contracts;
using Elsa.Alterations.Core.Entities;
using Elsa.Alterations.Core.Stores;
using Elsa.Common.Multitenancy;
using Elsa.Common.Services;
using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.EntityHandlers;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Alterations;
using Elsa.Persistence.EFCore.PostgreSql;
using Elsa.Tenants.Options;
using Elsa.Testing.Shared.Multitenancy;
using Elsa.Testing.Shared.Databases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Alterations.Persistence.ConformanceTests;

/// <summary>
/// Holds one Memory or EF/PostgreSQL pair of Alterations stores and the ambient tenant they read.
/// </summary>
public sealed class AlterationStoreScenario(
    TestTenantAccessor tenantAccessor,
    IAlterationPlanStore plans,
    IAlterationJobStore jobs,
    Func<ValueTask> disposeAsync) : IAsyncDisposable
{
    public TestTenantAccessor TenantAccessor { get; } = tenantAccessor;
    public IAlterationPlanStore Plans { get; } = plans;
    public IAlterationJobStore Jobs { get; } = jobs;

    public IDisposable UseTenant(string tenantId) =>
        TenantAccessor.PushContext(tenantId == Tenant.DefaultTenantId
            ? Tenant.Default
            : new Tenant { Id = tenantId, Name = tenantId });

    public ValueTask DisposeAsync() => disposeAsync();

    public static Task<AlterationStoreScenario> CreateInMemoryAsync()
    {
        var tenantAccessor = new TestTenantAccessor("tenant-a");
        var plans = new MemoryStore<AlterationPlan>();
        var jobs = new MemoryStore<AlterationJob>();

        return Task.FromResult(new AlterationStoreScenario(
            tenantAccessor,
            new MemoryAlterationPlanStore(plans, tenantAccessor),
            new MemoryAlterationJobStore(jobs, tenantAccessor),
            () => ValueTask.CompletedTask));
    }

    public static Task<AlterationStoreScenario> CreatePostgreSqlAsync() => CreatePostgreSqlAsync("tenant-a");

    public static Task<AlterationStoreScenario> CreatePostgreSqlAsync(string tenantId) =>
        CreatePostgreSqlAsync(tenantId, tenantsEnabled: true);

    public static async Task<AlterationStoreScenario> CreatePostgreSqlAsync(
        string tenantId,
        bool tenantsEnabled,
        DbCommandInterceptor? commandInterceptor = null,
        IDbExceptionHandler? dbExceptionHandler = null,
        DbTransactionInterceptor? transactionInterceptor = null)
    {
        var database = new PostgreSqlTestDatabase();
        try
        {
            var scenario = await CreatePostgreSqlHostAsync(tenantId, database.ConnectionString, tenantsEnabled,
                commandInterceptor, dbExceptionHandler, transactionInterceptor);
            return new(scenario.TenantAccessor, scenario.Plans, scenario.Jobs, async () =>
            {
                await scenario.DisposeAsync();
                await database.DisposeAsync();
            });
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public static async Task<PostgreSqlOwnershipPair> CreatePostgreSqlPairAsync(
        string firstTenantId,
        string secondTenantId,
        DbCommandInterceptor? commandInterceptor = null,
        DbTransactionInterceptor? transactionInterceptor = null)
    {
        var database = new PostgreSqlTestDatabase();
        var first = await CreatePostgreSqlHostAsync(firstTenantId, database.ConnectionString, true, commandInterceptor, null, transactionInterceptor);
        try
        {
            var second = await CreatePostgreSqlHostAsync(secondTenantId, database.ConnectionString, true, commandInterceptor, null, transactionInterceptor);
            return new(first, second, database);
        }
        catch
        {
            await first.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }
    }

    public static Task<AlterationStoreScenario> CreatePostgreSqlAsync(
        string tenantId,
        string connectionString,
        bool tenantsEnabled = true,
        DbCommandInterceptor? commandInterceptor = null,
        IDbExceptionHandler? dbExceptionHandler = null,
        DbTransactionInterceptor? transactionInterceptor = null) =>
        CreatePostgreSqlHostAsync(tenantId, connectionString, tenantsEnabled, commandInterceptor, dbExceptionHandler, transactionInterceptor);

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    private static async Task<AlterationStoreScenario> CreatePostgreSqlHostAsync(
        string tenantId,
        string connectionString,
        bool tenantsEnabled,
        DbCommandInterceptor? commandInterceptor,
        IDbExceptionHandler? dbExceptionHandler,
        DbTransactionInterceptor? transactionInterceptor)
    {
        var tenantAccessor = new TestTenantAccessor(tenantId);
        ServiceProvider? services = null;
        IServiceScope? scope = null;
        try
        {
            var migrationsAssembly = typeof(AlterationsDbContextFactories).Assembly;
            var serviceCollection = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ITenantAccessor>(tenantAccessor)
                .AddSingleton<IAlterationSerializer, ConformanceAlterationSerializer>()
                .Configure<TenantsOptions>(options => options.IsEnabled = tenantsEnabled)
                .AddScoped<IEntitySavingHandler, ApplyTenantId>()
                .AddScoped<IEntityModelCreatingHandler, SetTenantIdFilter>()
                .AddPostgreSqlEntityModelCreatingHandlers()
                .AddDbContextFactory<AlterationsElsaDbContext>((_, builder) =>
                {
                    builder.UseElsaPostgreSql(migrationsAssembly, connectionString);
                    builder.EnableServiceProviderCaching(false);
                    if (commandInterceptor is not null)
                        builder.AddInterceptors(commandInterceptor);
                    if (transactionInterceptor is not null)
                        builder.AddInterceptors(transactionInterceptor);
                })
                .Decorate<IDbContextFactory<AlterationsElsaDbContext>, TenantAwareDbContextFactory<AlterationsElsaDbContext>>()
                .AddScoped<EntityStore<AlterationsElsaDbContext, AlterationPlan>>()
                .AddScoped<EntityStore<AlterationsElsaDbContext, AlterationJob>>()
                .AddScoped<EFCoreAlterationPlanStore>()
                .AddScoped<EFCoreAlterationJobStore>();
            if (dbExceptionHandler is not null)
                serviceCollection.AddSingleton(dbExceptionHandler);
            services = serviceCollection.BuildServiceProvider();
            await using (var dbContext = await services.GetRequiredService<IDbContextFactory<AlterationsElsaDbContext>>().CreateDbContextAsync())
                await dbContext.Database.EnsureCreatedAsync();
            scope = services.CreateScope();
            return new(tenantAccessor,
                scope.ServiceProvider.GetRequiredService<EFCoreAlterationPlanStore>(),
                scope.ServiceProvider.GetRequiredService<EFCoreAlterationJobStore>(),
                async () => { scope.Dispose(); await services.DisposeAsync(); });
        }
        catch
        {
            scope?.Dispose();
            if (services is not null)
                await services.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// JSON stand-in for the real alteration serializer so EF can persist contract-visible fields.
    /// </summary>
    private sealed class ConformanceAlterationSerializer : IAlterationSerializer
    {
        public string Serialize(IAlteration alteration) => JsonSerializer.Serialize((TestAlteration)alteration, TestJsonSerializerContext.Default.TestAlteration);

        public string SerializeMany(IEnumerable<IAlteration> alterations) =>
            JsonSerializer.Serialize(alterations.Cast<TestAlteration>().ToArray(), TestJsonSerializerContext.Default.TestAlterationArray);

        public IAlteration Deserialize(string json) => JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.TestAlteration)!;

        public IEnumerable<IAlteration> DeserializeMany(string json) =>
            JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.TestAlterationArray)!;
    }
}

/// <summary>Two PostgreSQL alteration-store hosts sharing one isolated database.</summary>
public sealed class PostgreSqlOwnershipPair(AlterationStoreScenario first, AlterationStoreScenario second, PostgreSqlTestDatabase database) : IAsyncDisposable
{
    public AlterationStoreScenario First { get; } = first;
    public AlterationStoreScenario Second { get; } = second;
    public string ConnectionString => database.ConnectionString;

    public async ValueTask DisposeAsync()
    {
        await First.DisposeAsync();
        await Second.DisposeAsync();
        await database.DisposeAsync();
    }
}

/// <summary>
/// Minimal <see cref="IAlteration"/> used to lock save/load of serialized plan alterations.
/// </summary>
public sealed class TestAlteration : IAlteration
{
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
}
