using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Elsa.Common;
using Elsa.Common.Codecs;
using Elsa.Common.Multitenancy;
using Elsa.Common.Services;
using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.EntityHandlers;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Elsa.Persistence.EFCore.PostgreSql;
using Elsa.Tenants.Options;
using Elsa.Testing.Shared.Databases;
using Elsa.Testing.Shared.Multitenancy;
using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Stores;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Elsa.Workflows.Persistence.ConformanceTests;

/// <summary>
/// Holds one Memory or EF/PostgreSQL set of management + runtime stores and the ambient tenant they read.
/// </summary>
public sealed class WorkflowStoreScenario(
    TestTenantAccessor tenantAccessor,
    IWorkflowDefinitionStore definitions,
    ITriggerStore triggers,
    IBookmarkStore bookmarks,
    IBookmarkQueueDeadLetterStore deadLetters,
    IActivityExecutionStore activityExecutions,
    IWorkflowExecutionLogStore executionLogs,
    Func<Func<Task>, Task> assertUniquenessConflictAsync,
    Func<ValueTask> disposeAsync,
    IDbContextFactory<RuntimeElsaDbContext>? runtimeDbContextFactory = null) : IAsyncDisposable
{
    public TestTenantAccessor TenantAccessor { get; } = tenantAccessor;
    public IWorkflowDefinitionStore Definitions { get; } = definitions;
    public ITriggerStore Triggers { get; } = triggers;
    public IBookmarkStore Bookmarks { get; } = bookmarks;
    public IBookmarkQueueDeadLetterStore DeadLetters { get; } = deadLetters;
    public IActivityExecutionStore ActivityExecutions { get; } = activityExecutions;
    public IWorkflowExecutionLogStore ExecutionLogs { get; } = executionLogs;
    public IDbContextFactory<RuntimeElsaDbContext>? RuntimeDbContextFactory { get; } = runtimeDbContextFactory;

    public IDisposable UseTenant(string tenantId) =>
        TenantAccessor.PushContext(tenantId == Tenant.DefaultTenantId
            ? Tenant.Default
            : new Tenant { Id = tenantId, Name = tenantId });

    public Task AssertUniquenessConflictAsync(Func<Task> operation) => assertUniquenessConflictAsync(operation);

    public async Task ClearBookmarkTenantIdAsync(string id)
    {
        if (RuntimeDbContextFactory is null)
            throw new InvalidOperationException("Raw tenant updates require an EF runtime store.");

        await using var dbContext = await RuntimeDbContextFactory.CreateDbContextAsync();
        await dbContext.Bookmarks
            .IgnoreQueryFilters()
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TenantId, (string?)null));
    }

    public ValueTask DisposeAsync() => disposeAsync();

    public static Task<WorkflowStoreScenario> CreateInMemoryAsync()
    {
        var tenantAccessor = new TestTenantAccessor("tenant-a");
        return Task.FromResult(new WorkflowStoreScenario(
            tenantAccessor,
            new MemoryWorkflowDefinitionStore(new MemoryStore<WorkflowDefinition>(), tenantAccessor),
            new MemoryTriggerStore(new MemoryStore<StoredTrigger>(), tenantAccessor),
            new MemoryBookmarkStore(new MemoryStore<StoredBookmark>(), tenantAccessor),
            new MemoryBookmarkQueueDeadLetterStore(new MemoryStore<BookmarkQueueDeadLetterItem>()),
            new MemoryActivityExecutionStore(new MemoryStore<ActivityExecutionRecord>()),
            new MemoryWorkflowExecutionLogStore(new MemoryStore<WorkflowExecutionLogRecord>()),
            async operation => { await Assert.ThrowsExactlyAsync<InvalidOperationException>(operation); },
            () => ValueTask.CompletedTask));
    }

    public static Task<WorkflowStoreScenario> CreatePostgreSqlAsync() => CreatePostgreSqlAsync("tenant-a");

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    public static async Task<WorkflowStoreScenario> CreatePostgreSqlAsync(string tenantId, bool tenantsEnabled = true)
    {
        var managementDatabase = new PostgreSqlTestDatabase();
        var runtimeDatabase = new PostgreSqlTestDatabase();
        var tenantAccessor = new TestTenantAccessor(tenantId);
        ServiceProvider? services = null;
        IServiceScope? scope = null;

        try
        {
            var migrationsAssembly = typeof(ManagementDbContextFactory).Assembly;
            services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ITenantAccessor>(tenantAccessor)
                .AddSingleton<IPayloadSerializer, ConformancePayloadSerializer>()
                .AddSingleton<ISafeSerializer, ConformanceSafeSerializer>()
                .AddSingleton<ICompressionCodecResolver>(_ => new CompressionCodecResolver([new None()]))
                .Configure<TenantsOptions>(options => options.IsEnabled = tenantsEnabled)
                .AddScoped<IEntitySavingHandler, ApplyTenantId>()
                .AddScoped<IEntityModelCreatingHandler, SetTenantIdFilter>()
                .AddPostgreSqlEntityModelCreatingHandlers()
                // The disabled-tenancy contract has a different EF model (no tenant query filters).
                // Keep it out of the shared provider/model cache used by the parallel tenant-aware scenarios.
                .AddDbContextFactory<ManagementElsaDbContext>((_, builder) =>
                    builder.UseElsaPostgreSql(migrationsAssembly, managementDatabase.ConnectionString)
                        .EnableServiceProviderCaching(tenantsEnabled))
                .AddDbContextFactory<RuntimeElsaDbContext>((_, builder) =>
                    builder.UseElsaPostgreSql(migrationsAssembly, runtimeDatabase.ConnectionString)
                        .EnableServiceProviderCaching(tenantsEnabled))
                .Decorate<IDbContextFactory<ManagementElsaDbContext>, TenantAwareDbContextFactory<ManagementElsaDbContext>>()
                .Decorate<IDbContextFactory<RuntimeElsaDbContext>, TenantAwareDbContextFactory<RuntimeElsaDbContext>>()
                .AddScoped<EntityStore<ManagementElsaDbContext, WorkflowDefinition>>()
                .AddScoped<EntityStore<RuntimeElsaDbContext, StoredTrigger>>()
                .AddScoped<Store<RuntimeElsaDbContext, StoredBookmark>>()
                .AddScoped<Store<RuntimeElsaDbContext, BookmarkQueueDeadLetterItem>>()
                .AddScoped<EntityStore<RuntimeElsaDbContext, ActivityExecutionRecord>>()
                .AddScoped<EntityStore<RuntimeElsaDbContext, WorkflowExecutionLogRecord>>()
                .AddScoped<EFCoreWorkflowDefinitionStore>()
                .AddScoped<EFCoreTriggerStore>()
                .AddScoped<EFCoreBookmarkStore>()
                .AddScoped<EFBookmarkQueueDeadLetterStore>()
                .AddScoped<EFCoreActivityExecutionStore>()
                .AddScoped<EFCoreWorkflowExecutionLogStore>()
                .BuildServiceProvider();

            await using (var management = await services.GetRequiredService<IDbContextFactory<ManagementElsaDbContext>>().CreateDbContextAsync())
                await management.Database.EnsureCreatedAsync();
            await using (var runtime = await services.GetRequiredService<IDbContextFactory<RuntimeElsaDbContext>>().CreateDbContextAsync())
                await runtime.Database.EnsureCreatedAsync();

            scope = services.CreateScope();
            var scoped = scope.ServiceProvider;

            return new(
                tenantAccessor,
                scoped.GetRequiredService<EFCoreWorkflowDefinitionStore>(),
                scoped.GetRequiredService<EFCoreTriggerStore>(),
                scoped.GetRequiredService<EFCoreBookmarkStore>(),
                scoped.GetRequiredService<EFBookmarkQueueDeadLetterStore>(),
                scoped.GetRequiredService<EFCoreActivityExecutionStore>(),
                scoped.GetRequiredService<EFCoreWorkflowExecutionLogStore>(),
                AssertPostgreSqlUniquenessConflictAsync,
                async () =>
                {
                    scope.Dispose();
                    await services.DisposeAsync();
                    await managementDatabase.DisposeAsync();
                    await runtimeDatabase.DisposeAsync();
                },
                scoped.GetRequiredService<IDbContextFactory<RuntimeElsaDbContext>>());
        }
        catch
        {
            scope?.Dispose();
            if (services is not null)
                await services.DisposeAsync();
            await managementDatabase.DisposeAsync();
            await runtimeDatabase.DisposeAsync();
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

    private sealed class ConformancePayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The EF store passes its private workflow-definition state through this JIT-only conformance serializer; tests cannot generate metadata for that production type.")]
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;

        public JsonSerializerOptions GetOptions() => Options;
    }

    private sealed class ConformanceSafeSerializer : ISafeSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        public ValueTask<string> SerializeAsync(object? value, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Serialize(value));

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        public ValueTask<JsonElement> SerializeToElementAsync(object? value, CancellationToken cancellationToken = default) =>
            new(SerializeToElement(value));

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        public ValueTask<T> DeserializeAsync<T>(string json, CancellationToken cancellationToken = default) =>
            new(Deserialize<T>(json));

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        public ValueTask<T> DeserializeAsync<T>(JsonElement element, CancellationToken cancellationToken = default) =>
            new(Deserialize<T>(element));

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-only conformance serializer retains the production interface trimming contract and reflection-based JSON behavior.")]
        public string Serialize(object? value) => JsonSerializer.Serialize(value, Options);

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-only conformance serializer retains the production interface trimming contract and reflection-based JSON behavior.")]
        public JsonElement SerializeToElement(object? value) => JsonSerializer.SerializeToElement(value, Options);

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-only conformance serializer retains the production interface trimming contract and reflection-based JSON behavior.")]
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

        [RequiresUnreferencedCode("The type T may be trimmed.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-only conformance serializer retains the production interface trimming contract and reflection-based JSON behavior.")]
        public T Deserialize<T>(JsonElement element) => element.Deserialize<T>(Options)!;

        public JsonSerializerOptions GetOptions() => Options;
    }
}
