using System.Text.Json;
using Elsa.Common.Multitenancy;
using Elsa.Testing.Shared.Multitenancy;
using Elsa.Workflows;
using Elsa.Workflows.Api.Endpoints.WorkflowInstances.Import;
using Elsa.Workflows.Api.Models;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.State;

namespace Elsa.Workflows.Persistence.ConformanceTests;

/// <summary>
/// EF <c>Store.SaveManyAsync</c> must refuse a cross-tenant key collision before any row is written.
/// </summary>
public sealed class EFCoreSaveManyTenantOwnershipTests
{
    [Test]
    public async Task SaveManyAsync_WhenOtherTenantOwnsId_ThrowsAndLeavesOwnerAndPayload()
    {
        await using var owner = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a");
        await owner.Bookmarks.SaveManyAsync([Bookmark("shared", "tenant-a", "original")], CancellationToken.None);

        using (owner.UseTenant("tenant-b"))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                owner.Bookmarks.SaveManyAsync([Bookmark("shared", "tenant-b", "stolen")], CancellationToken.None).AsTask());
        }

        var remaining = await FindBookmarkAsync(owner, "shared");
        await AssertUnchangedAsync(remaining, "tenant-a", "original");
    }

    [Test]
    public async Task SaveManyAsync_WhenMixedBatchContainsForeignId_WritesNothing()
    {
        await using var owner = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a");
        await owner.Bookmarks.SaveManyAsync([Bookmark("owned", "tenant-a", "original")], CancellationToken.None);

        using (owner.UseTenant("tenant-b"))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => owner.Bookmarks.SaveManyAsync(
            [
                Bookmark("new-from-b", "tenant-b", "should-not-land"),
                Bookmark("owned", "tenant-b", "stolen")
            ], CancellationToken.None).AsTask());

            await Assert.That(await FindBookmarkAsync(owner, "new-from-b")).IsNull();
        }

        var remaining = await FindBookmarkAsync(owner, "owned");
        await AssertUnchangedAsync(remaining, "tenant-a", "original");
    }

    [Test]
    public async Task SaveManyAsync_WhenSameTenantOwnsId_UpdatesPayload()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a");
        await scenario.Bookmarks.SaveManyAsync([Bookmark("bm-a", "tenant-a", "before")], CancellationToken.None);

        await scenario.Bookmarks.SaveManyAsync([Bookmark("bm-a", "tenant-a", "after")], CancellationToken.None);

        var found = await FindBookmarkAsync(scenario, "bm-a");
        await AssertUnchangedAsync(found, "tenant-a", "after");
    }

    [Test]
    public async Task SaveManyAsync_WhenPopulatorResavesStarUnderNamedTenant_KeepsStar()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a");
        await scenario.Bookmarks.SaveManyAsync([Bookmark("shared", Tenant.AgnosticTenantId, "before")], CancellationToken.None);

        await scenario.Bookmarks.SaveManyAsync([Bookmark("shared", Tenant.AgnosticTenantId, "after")], CancellationToken.None);

        var found = await FindBookmarkAsync(scenario, "shared");
        await AssertUnchangedAsync(found, Tenant.AgnosticTenantId, "after");
    }

    [Test]
    public async Task SaveManyAsync_WhenNamedTenantSavesNullTenantIdOverStar_ThrowsAndLeavesStar()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a");
        await scenario.Bookmarks.SaveManyAsync([Bookmark("shared", Tenant.AgnosticTenantId, "original")], CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            scenario.Bookmarks.SaveManyAsync([Bookmark("shared", tenantId: null, hash: "stolen")], CancellationToken.None).AsTask());

        var remaining = await FindBookmarkAsync(scenario, "shared");
        await AssertUnchangedAsync(remaining, Tenant.AgnosticTenantId, "original");
    }

    [Test]
    public async Task SaveManyAsync_WhenDefaultTenantUpdatesLegacyNullRow_Succeeds()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync(Tenant.DefaultTenantId);
        await scenario.Bookmarks.SaveManyAsync([Bookmark("legacy", Tenant.DefaultTenantId, "before")], CancellationToken.None);
        await scenario.ClearBookmarkTenantIdAsync("legacy");

        await scenario.Bookmarks.SaveManyAsync([Bookmark("legacy", Tenant.DefaultTenantId, "after")], CancellationToken.None);

        var found = await FindBookmarkAsync(scenario, "legacy");
        await Assert.That(found).IsNotNull();
        await Assert.That(found.Hash).IsEqualTo("after");
        await Assert.That(string.IsNullOrEmpty(found.TenantId)).IsTrue();
    }

    [Test]
    public async Task SaveManyAsync_WhenTenancyIsDisabled_OverwritesForeignId()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-b", tenantsEnabled: false);
        await scenario.Bookmarks.SaveManyAsync([Bookmark("shared", "tenant-a", "original")], CancellationToken.None);

        await scenario.Bookmarks.SaveManyAsync([Bookmark("shared", "tenant-b", "taken")], CancellationToken.None);

        var found = await FindBookmarkAsync(scenario, "shared");
        await AssertUnchangedAsync(found, "tenant-b", "taken");
    }

    [Test]
    public async Task Import_WhenBookmarkHasForeignId_Refuses_AndForeignRecordTenantIdIsStampedToWriter()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a");
        await scenario.Bookmarks.SaveManyAsync([Bookmark("foreign-bookmark", "tenant-a", "original")], CancellationToken.None);

        using (scenario.UseTenant("tenant-b"))
        {
            var endpoint = CreateImportEndpoint(scenario);
            var serializer = new ConformanceSafeSerializer();

            await ImportAsync(endpoint, new ExportedWorkflowState(
                JsonSerializer.SerializeToElement(new { }),
                Bookmarks: null,
                ActivityExecutionRecords: serializer.SerializeToElement(new[]
                {
                    ActivityExecution("ae-forged", "tenant-a")
                }),
                WorkflowExecutionLogRecords: serializer.SerializeToElement(new[]
                {
                    ExecutionLog("el-forged", "tenant-a")
                })));

            var activity = (await Assert.That(await scenario.ActivityExecutions.FindManyAsync(new ActivityExecutionRecordFilter { Id = "ae-forged" })).HasSingleItem());
            await Assert.That(activity.TenantId).IsEqualTo("tenant-b");

            var log = (await Assert.That((await scenario.ExecutionLogs.FindManyAsync(new WorkflowExecutionLogRecordFilter { Id = "el-forged" }, Elsa.Common.Models.PageArgs.All)).Items).HasSingleItem());
            await Assert.That(log.TenantId).IsEqualTo("tenant-b");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ImportAsync(endpoint, new ExportedWorkflowState(
                    JsonSerializer.SerializeToElement(new { }),
                    Bookmarks: ForeignBookmarkElement("foreign-bookmark"),
                    ActivityExecutionRecords: null,
                    WorkflowExecutionLogRecords: null)));
        }

        var remaining = await FindBookmarkAsync(scenario, "foreign-bookmark");
        await AssertUnchangedAsync(remaining, "tenant-a", "original");
    }

    [Test]
    public async Task Import_StampsActivityAndLogRecordsWithImportingTenant_OnNonStampingStore()
    {
        var tenantAccessor = new TestTenantAccessor("tenant-b");
        IReadOnlyList<ActivityExecutionRecord>? savedActivities = null;
        IReadOnlyList<WorkflowExecutionLogRecord>? savedLogs = null;
        var activityStore = IActivityExecutionStore.Mock().Object;
        Mock.Get(activityStore).SaveManyAsync(Arg.Any<IEnumerable<ActivityExecutionRecord>>(), Arg.Any<CancellationToken>())
            .Callback((IEnumerable<ActivityExecutionRecord> records, CancellationToken cancellationToken) => savedActivities = records.ToList())
            .ReturnsAsync(Task.CompletedTask);
        var logStore = IWorkflowExecutionLogStore.Mock().Object;
        Mock.Get(logStore).SaveManyAsync(Arg.Any<IEnumerable<WorkflowExecutionLogRecord>>(), Arg.Any<CancellationToken>())
            .Callback((IEnumerable<WorkflowExecutionLogRecord> records, CancellationToken cancellationToken) => savedLogs = records.ToList())
            .ReturnsAsync(Task.CompletedTask);
        var serializer = new ConformanceSafeSerializer();

        await ImportAsync(CreateImportEndpoint(tenantAccessor, activityStore, logStore), new ExportedWorkflowState(
            JsonSerializer.SerializeToElement(new { }),
            Bookmarks: null,
            ActivityExecutionRecords: serializer.SerializeToElement(new[] { ActivityExecution("ae-1", "forged") }),
            WorkflowExecutionLogRecords: serializer.SerializeToElement(new[] { ExecutionLog("el-1", "forged") })));

        await Assert.That((await Assert.That(savedActivities!).HasSingleItem()).TenantId).IsEqualTo("tenant-b");
        await Assert.That((await Assert.That(savedLogs!).HasSingleItem()).TenantId).IsEqualTo("tenant-b");
    }

    private static async Task<StoredBookmark?> FindBookmarkAsync(WorkflowStoreScenario scenario, string id) =>
        await scenario.Bookmarks.FindAsync(new BookmarkFilter { BookmarkId = id, TenantAgnostic = true });

    private static async Task AssertUnchangedAsync(StoredBookmark? bookmark, string tenantId, string hash)
    {
        await Assert.That(bookmark).IsNotNull();
        await Assert.That(bookmark.TenantId).IsEqualTo(tenantId);
        await Assert.That(bookmark.Hash).IsEqualTo(hash);
    }

    private static StoredBookmark Bookmark(string id, string? tenantId, string hash) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Hash = hash,
            WorkflowInstanceId = "instance-1",
            Name = "Elsa.HttpEndpoint",
            CreatedAt = CreatedAt
        };

    private static ActivityExecutionRecord ActivityExecution(string id, string? tenantId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            WorkflowInstanceId = "instance-1",
            ActivityId = "activity-1",
            ActivityNodeId = "node-1",
            ActivityType = "Elsa.WriteLine",
            ActivityTypeVersion = 1,
            Status = Workflows.ActivityStatus.Running,
            StartedAt = CreatedAt
        };

    private static WorkflowExecutionLogRecord ExecutionLog(string id, string? tenantId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            WorkflowDefinitionId = "workflow-1",
            WorkflowDefinitionVersionId = "v1",
            WorkflowInstanceId = "instance-1",
            WorkflowVersion = 1,
            ActivityInstanceId = "ai-1",
            ActivityId = "activity-1",
            ActivityType = "Elsa.WriteLine",
            ActivityTypeVersion = 1,
            ActivityNodeId = "node-1",
            Timestamp = CreatedAt,
            EventName = "Started"
        };

    private static Import CreateImportEndpoint(WorkflowStoreScenario scenario) =>
        CreateImportEndpoint(scenario.TenantAccessor, scenario.ActivityExecutions, scenario.ExecutionLogs, scenario.Bookmarks);

    private static Import CreateImportEndpoint(
        ITenantAccessor tenantAccessor,
        IActivityExecutionStore activityExecutions,
        IWorkflowExecutionLogStore executionLogs,
        IBookmarkStore? bookmarks = null)
    {
        var stateSerializer = IWorkflowStateSerializer.Mock().Object;
        Mock.Get(stateSerializer).Deserialize(Arg.Any<JsonElement>()).Returns(new WorkflowState
        {
            Id = "instance-1",
            DefinitionId = "workflow-1",
            DefinitionVersionId = "v1"
        });

        var instanceManager = IWorkflowInstanceManager.Mock().Object;
        Mock.Get(instanceManager).SaveAsync(Arg.Any<WorkflowState>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstance { Id = "instance-1" });

        return new Import(
            instanceManager,
            IWorkflowInstanceStore.Mock().Object,
            activityExecutions,
            executionLogs,
            bookmarks ?? IBookmarkStore.Mock().Object,
            stateSerializer,
            new ConformancePayloadSerializer(),
            new ConformanceSafeSerializer(),
            tenantAccessor);
    }

    private static async Task ImportAsync(Import endpoint, ExportedWorkflowState model)
    {
        var method = typeof(Import).GetMethod("ImportSingleWorkflowInstanceAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(endpoint, [model, CancellationToken.None])!;
    }

    private static JsonElement ForeignBookmarkElement(string id) =>
        JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                id,
                activityTypeName = "Elsa.HttpEndpoint",
                workflowInstanceId = "instance-1",
                activityInstanceId = (string?)null,
                hash = "stolen",
                correlationId = (string?)null,
                createdAt = CreatedAt,
                payload = new { },
                metadata = new Dictionary<string, string>()
            }
        });

    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class ConformancePayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
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

        public ValueTask<string> SerializeAsync(object? value, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Serialize(value));

        public ValueTask<JsonElement> SerializeToElementAsync(object? value, CancellationToken cancellationToken = default) =>
            new(SerializeToElement(value));

        public ValueTask<T> DeserializeAsync<T>(string json, CancellationToken cancellationToken = default) =>
            new(Deserialize<T>(json));

        public ValueTask<T> DeserializeAsync<T>(JsonElement element, CancellationToken cancellationToken = default) =>
            new(Deserialize<T>(element));

        public string Serialize(object? value) => JsonSerializer.Serialize(value, Options);
        public JsonElement SerializeToElement(object? value) => JsonSerializer.SerializeToElement(value, Options);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
        public T Deserialize<T>(JsonElement element) => element.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => Options;
    }
}
