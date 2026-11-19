using Flamme.Common.Multitenancy;
using Flamme.Common.Services;
using Flamme.Labels.Contracts;
using Flamme.Labels.Entities;
using Flamme.Labels.Services;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.EntityHandlers;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Labels;
using Flamme.Persistence.EFCore.PostgreSql;
using Flamme.Tenants.Options;
using Flamme.Testing.Shared.Databases;
using Flamme.Testing.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flamme.Labels.Persistence.ConformanceTests;

/// <summary>
/// Holds one InMemory or EF/PostgreSQL pair of Labels stores and the ambient tenant they read.
/// </summary>
public sealed class LabelStoreScenario(
    TestTenantAccessor tenantAccessor,
    ILabelStore labels,
    IWorkflowDefinitionLabelStore associations,
    IWorkflowDefinitionLabelQuery associationQuery,
    Func<Func<Task>, Task> assertUniquenessConflictAsync,
    Func<ValueTask> disposeAsync) : IAsyncDisposable
{
    public TestTenantAccessor TenantAccessor { get; } = tenantAccessor;
    public ILabelStore Labels { get; } = labels;
    public IWorkflowDefinitionLabelStore Associations { get; } = associations;
    public IWorkflowDefinitionLabelQuery AssociationQuery { get; } = associationQuery;

    public IDisposable UseTenant(string tenantId) =>
        TenantAccessor.PushContext(tenantId == Tenant.DefaultTenantId
            ? Tenant.Default
            : new Tenant { Id = tenantId, Name = tenantId });

    public Task AssertUniquenessConflictAsync(Func<Task> operation) => assertUniquenessConflictAsync(operation);

    public ValueTask DisposeAsync() => disposeAsync();

    public static Task<LabelStoreScenario> CreateInMemoryAsync()
    {
        var tenantAccessor = new TestTenantAccessor("tenant-a");
        var labels = new MemoryStore<Label>();
        var associations = new MemoryStore<WorkflowDefinitionLabel>();
        var labelStore = new InMemoryLabelStore(labels, associations, tenantAccessor);
        var associationStore = new InMemoryWorkflowDefinitionLabelStore(associations, tenantAccessor);

        return Task.FromResult(new LabelStoreScenario(
            tenantAccessor,
            labelStore,
            associationStore,
            associationStore,
            async operation => { await Assert.ThrowsExactlyAsync<InvalidOperationException>(operation); },
            () => ValueTask.CompletedTask));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    public static async Task<LabelStoreScenario> CreatePostgreSqlAsync()
    {
        var database = new PostgreSqlTestDatabase();
        var tenantAccessor = new TestTenantAccessor("tenant-a");
        ServiceProvider? services = null;
        IServiceScope? scope = null;

        try
        {
            var migrationsAssembly = typeof(LabelsDbContextFactory).Assembly;
            services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ITenantAccessor>(tenantAccessor)
                .Configure<TenantsOptions>(options => options.IsEnabled = true)
                .AddScoped<IEntitySavingHandler, ApplyTenantId>()
                .AddScoped<IEntityModelCreatingHandler, SetTenantIdFilter>()
                .AddPostgreSqlEntityModelCreatingHandlers()
                .AddDbContextFactory<LabelsFlammeDbContext>((_, builder) =>
                    builder.UseFlammePostgreSql(migrationsAssembly, database.ConnectionString))
                .Decorate<IDbContextFactory<LabelsFlammeDbContext>, TenantAwareDbContextFactory<LabelsFlammeDbContext>>()
                .AddScoped<EntityStore<LabelsFlammeDbContext, Label>>()
                .AddScoped<EntityStore<LabelsFlammeDbContext, WorkflowDefinitionLabel>>()
                .AddScoped<EFCoreLabelStore>()
                .AddScoped<EFCoreWorkflowDefinitionLabelStore>()
                .BuildServiceProvider();

            await using (var dbContext = await services.GetRequiredService<IDbContextFactory<LabelsFlammeDbContext>>().CreateDbContextAsync())
                await dbContext.Database.EnsureCreatedAsync();

            scope = services.CreateScope();
            var scoped = scope.ServiceProvider;
            var associationStore = scoped.GetRequiredService<EFCoreWorkflowDefinitionLabelStore>();

            return new(
                tenantAccessor,
                scoped.GetRequiredService<EFCoreLabelStore>(),
                associationStore,
                associationStore,
                AssertPostgreSqlUniquenessConflictAsync,
                async () =>
                {
                    scope.Dispose();
                    await services.DisposeAsync();
                    await database.DisposeAsync();
                });
        }
        catch
        {
            scope?.Dispose();
            if (services is not null)
                await services.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task AssertPostgreSqlUniquenessConflictAsync(Func<Task> operation)
    {
        var exception = await CaptureExceptionAsync(operation);
        var postgresException = exception switch
        {
            DbUpdateException { InnerException: PostgresException inner } => inner,
            PostgresException direct => direct,
            _ => throw new TUnit.Assertions.Exceptions.AssertionException($"Expected a PostgreSQL uniqueness violation, received {exception?.GetType().FullName ?? "no exception"}.")
        };

        await Assert.That(postgresException.SqlState).IsEqualTo("23505");
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
