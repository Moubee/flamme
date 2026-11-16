using Elsa.Common.Entities;
using Elsa.Common.Models;
using Elsa.Common.Multitenancy;
using Elsa.Workflows;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.OrderDefinitions;

namespace Elsa.Workflows.Persistence.ConformanceTests;

/// <summary>
/// Shared Memory / EF Core store-contract assertions for workflow management and runtime ports.
/// </summary>
public abstract class WorkflowStoreConformanceTests
{
    protected abstract Task<WorkflowStoreScenario> CreateScenarioAsync();

    [Test]
    public async Task TriggerLogicalKeysAreUniqueAndReplaceAsyncSkipsExistingKeys()
    {
        await using var scenario = await CreateScenarioAsync();
        var existing = Trigger("existing-id", hash: "hash-1");

        await scenario.Triggers.SaveAsync(existing);
        await scenario.AssertUniquenessConflictAsync(() => scenario.Triggers.SaveAsync(Trigger("other-id", hash: "hash-1")).AsTask());

        var afterConflict = (await scenario.Triggers.FindManyAsync(new TriggerFilter { TenantAgnostic = true })).ToList();
        await Assert.That((await Assert.That(afterConflict).HasSingleItem()).Id).IsEqualTo("existing-id");

        await scenario.Triggers.SaveAsync(Trigger("existing-id", hash: "hash-2", workflowDefinitionVersionId: "v2"));
        var updated = await scenario.Triggers.FindAsync(new TriggerFilter { Id = "existing-id" });
        await Assert.That(updated!.Hash).IsEqualTo("hash-2");
        await Assert.That(updated.WorkflowDefinitionVersionId).IsEqualTo("v2");

        await scenario.Triggers.ReplaceAsync([], [Trigger("batch-1"), Trigger("batch-2")]);
        var afterBatch = (await scenario.Triggers.FindManyAsync(new TriggerFilter { Hash = "hash-1", TenantAgnostic = true })).ToList();
        await Assert.That((await Assert.That(afterBatch).HasSingleItem()).Id).IsEqualTo("batch-1");

        await scenario.Triggers.ReplaceAsync([], [Trigger("skipped-id")]);
        var afterSkip = (await scenario.Triggers.FindManyAsync(new TriggerFilter { Hash = "hash-1", TenantAgnostic = true })).ToList();
        await Assert.That((await Assert.That(afterSkip).HasSingleItem()).Id).IsEqualTo("batch-1");

        await scenario.Triggers.ReplaceAsync(afterSkip, [Trigger("replacement-id", workflowDefinitionVersionId: "v3")]);
        var afterReplace = (await scenario.Triggers.FindManyAsync(new TriggerFilter { Hash = "hash-1", TenantAgnostic = true })).ToList();
        var replacement = (await Assert.That(afterReplace).HasSingleItem());
        await Assert.That(replacement.Id).IsEqualTo("replacement-id");
        await Assert.That(replacement.WorkflowDefinitionVersionId).IsEqualTo("v3");

        var tenantA = Trigger("id-a", hash: "shared-hash");
        tenantA.TenantId = "tenant-a";
        var tenantB = Trigger("id-b", hash: "shared-hash");
        tenantB.TenantId = "tenant-b";
        await scenario.Triggers.ReplaceAsync([], [tenantA, tenantB]);
        var bothTenants = (await scenario.Triggers.FindManyAsync(new TriggerFilter { Hash = "shared-hash", TenantAgnostic = true })).ToList();
        await Assert.That(bothTenants.Count).IsEqualTo(2);
        await Assert.That(bothTenants).Contains(x => x.Id == "id-a");
        await Assert.That(bothTenants).Contains(x => x.Id == "id-b");
    }

    [Test]
    public async Task TriggerTenantIsolationHonorsAmbientTenantAndTenantAgnostic()
    {
        await using var scenario = await CreateScenarioAsync();
        await SeedMixedTriggersAsync(scenario);

        var visible = (await scenario.Triggers.FindManyAsync(new TriggerFilter())).ToList();
        await Assert.That(visible.Count).IsEqualTo(2);
        await Assert.That(visible).Contains(x => x.Id == "id-a");
        await Assert.That(visible).Contains(x => x.Id == "id-star");
        await Assert.That(visible).DoesNotContain(x => x.Id == "id-b");

        var all = (await scenario.Triggers.FindManyAsync(new TriggerFilter { TenantAgnostic = true })).ToList();
        await Assert.That(all.Count).IsEqualTo(3);
        await Assert.That(all).Contains(x => x.Id == "id-b");

        await Assert.That(await scenario.Triggers.FindAsync(new TriggerFilter { Id = "id-b" })).IsNull();

        var deleted = await scenario.Triggers.DeleteManyAsync(new TriggerFilter());
        var remaining = (await scenario.Triggers.FindManyAsync(new TriggerFilter { TenantAgnostic = true })).ToList();
        await Assert.That(deleted).IsEqualTo(2);
        await Assert.That((await Assert.That(remaining).HasSingleItem()).Id).IsEqualTo("id-b");

        using (scenario.UseTenant(Tenant.DefaultTenantId))
        {
            await scenario.Triggers.SaveAsync(Trigger("id-null", hash: "hash-null", tenantId: null));
            await scenario.Triggers.SaveAsync(Trigger("id-named", hash: "hash-named", tenantId: "tenant-a"));

            var defaultVisible = (await scenario.Triggers.FindManyAsync(new TriggerFilter())).ToList();
            await Assert.That(defaultVisible).Contains(x => x.Id == "id-null");
            await Assert.That(defaultVisible).DoesNotContain(x => x.Id == "id-named");
        }

        using (scenario.UseTenant("tenant-a"))
        {
            var namedVisible = (await scenario.Triggers.FindManyAsync(new TriggerFilter { Hash = "hash-null" })).ToList();
            await Assert.That(namedVisible).IsEmpty();
        }
    }

    [Test]
    public async Task DefinitionVersionsAreUniquePerDefinitionId()
    {
        await using var scenario = await CreateScenarioAsync();
        var first = Definition("def-v1", "order", "tenant-a");

        await scenario.Definitions.SaveAsync(first);
        await scenario.AssertUniquenessConflictAsync(() => scenario.Definitions.SaveAsync(Definition("def-v1-dup", "order", "tenant-a")));

        var afterConflict = (await scenario.Definitions.FindManyAsync(new WorkflowDefinitionFilter
        {
            DefinitionId = "order",
            VersionOptions = VersionOptions.SpecificVersion(1),
            TenantAgnostic = true
        })).ToList();
        await Assert.That((await Assert.That(afterConflict).HasSingleItem()).Id).IsEqualTo("def-v1");

        first.Name = "Order Updated";
        await scenario.Definitions.SaveAsync(first);
        var updated = await scenario.Definitions.FindAsync(new WorkflowDefinitionFilter { Id = "def-v1" });
        await Assert.That(updated!.Name).IsEqualTo("Order Updated");

        await scenario.Definitions.SaveAsync(Definition("def-v2", "order", "tenant-a", version: 2));
        var versions = (await scenario.Definitions.FindManyAsync(new WorkflowDefinitionFilter { DefinitionId = "order", TenantAgnostic = true })).ToList();
        await Assert.That(versions.Count).IsEqualTo(2);
        await Assert.That(versions).Contains(x => x.Id == "def-v1" && x.Version == 1);
        await Assert.That(versions).Contains(x => x.Id == "def-v2" && x.Version == 2);
    }

    [Test]
    public async Task DefinitionSaveManyRejectsDuplicateVersionKeysInTheBatch()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.Definitions.SaveAsync(Definition("def-kept", "kept", "tenant-a"));

        await scenario.AssertUniquenessConflictAsync(() => scenario.Definitions.SaveManyAsync(
        [
            Definition("def-batch-1", "invoice", "tenant-a"),
            Definition("def-batch-2", "invoice", "tenant-a")
        ]));

        var invoices = (await scenario.Definitions.FindManyAsync(new WorkflowDefinitionFilter
        {
            DefinitionId = "invoice",
            TenantAgnostic = true
        })).ToList();
        await Assert.That(invoices).IsEmpty();
        await Assert.That(await scenario.Definitions.FindAsync(new WorkflowDefinitionFilter { Id = "def-kept", TenantAgnostic = true })).IsNotNull();
    }

    [Test]
    public async Task DefinitionTenantIsolationHonorsAmbientTenantAndTenantAgnostic()
    {
        await using var scenario = await CreateScenarioAsync();
        await SeedMixedDefinitionsAsync(scenario);

        var visible = (await scenario.Definitions.FindManyAsync(new WorkflowDefinitionFilter())).ToList();
        await Assert.That(visible.Count).IsEqualTo(2);
        await Assert.That(visible).Contains(x => x.Id == "def-a");
        await Assert.That(visible).Contains(x => x.Id == "def-star");
        await Assert.That(visible).DoesNotContain(x => x.Id == "def-b");

        var all = (await scenario.Definitions.FindManyAsync(new WorkflowDefinitionFilter { TenantAgnostic = true })).ToList();
        await Assert.That(all.Count).IsEqualTo(3);
        await Assert.That(all).Contains(x => x.Id == "def-b");

        await Assert.That(await scenario.Definitions.FindAsync(new WorkflowDefinitionFilter { Id = "def-b" })).IsNull();
        await Assert.That(await scenario.Definitions.AnyAsync(new WorkflowDefinitionFilter { Id = "def-b" })).IsFalse();
        await Assert.That(await scenario.Definitions.CountDistinctAsync()).IsEqualTo(2);

        var deleted = await scenario.Definitions.DeleteAsync(new WorkflowDefinitionFilter());
        var remaining = (await scenario.Definitions.FindManyAsync(new WorkflowDefinitionFilter { TenantAgnostic = true })).ToList();
        await Assert.That(deleted).IsEqualTo(2);
        await Assert.That((await Assert.That(remaining).HasSingleItem()).Id).IsEqualTo("def-b");

        using (scenario.UseTenant(Tenant.DefaultTenantId))
        {
            await scenario.Definitions.SaveAsync(Definition("def-null", "Null", tenantId: null));
            await scenario.Definitions.SaveAsync(Definition("def-named", "Named", "tenant-a"));

            var defaultVisible = (await scenario.Definitions.FindManyAsync(new WorkflowDefinitionFilter())).ToList();
            await Assert.That(defaultVisible).Contains(x => x.Id == "def-null");
            await Assert.That(defaultVisible).DoesNotContain(x => x.Id == "def-named");
        }
    }

    [Test]
    public async Task BookmarkTenantIsolationHonorsAmbientTenantAndTenantAgnostic()
    {
        await using var scenario = await CreateScenarioAsync();
        await SeedMixedBookmarksAsync(scenario);

        var visible = (await scenario.Bookmarks.FindManyAsync(new BookmarkFilter())).ToList();
        await Assert.That(visible.Count).IsEqualTo(2);
        await Assert.That(visible).Contains(x => x.Id == "bm-a");
        await Assert.That(visible).Contains(x => x.Id == "bm-star");
        await Assert.That(visible).DoesNotContain(x => x.Id == "bm-b");

        var all = (await scenario.Bookmarks.FindManyAsync(new BookmarkFilter { TenantAgnostic = true })).ToList();
        await Assert.That(all.Count).IsEqualTo(3);
        await Assert.That(all).Contains(x => x.Id == "bm-b");

        await Assert.That(await scenario.Bookmarks.FindAsync(new BookmarkFilter { BookmarkId = "bm-b" })).IsNull();

        var deleted = await scenario.Bookmarks.DeleteAsync(new BookmarkFilter());
        var remaining = (await scenario.Bookmarks.FindManyAsync(new BookmarkFilter { TenantAgnostic = true })).ToList();
        await Assert.That(deleted).IsEqualTo(2);
        await Assert.That((await Assert.That(remaining).HasSingleItem()).Id).IsEqualTo("bm-b");

        using (scenario.UseTenant(Tenant.DefaultTenantId))
        {
            await scenario.Bookmarks.SaveAsync(Bookmark("bm-null", tenantId: null));
            await scenario.Bookmarks.SaveAsync(Bookmark("bm-named", tenantId: "tenant-a"));

            var defaultVisible = (await scenario.Bookmarks.FindManyAsync(new BookmarkFilter())).ToList();
            await Assert.That(defaultVisible).Contains(x => x.Id == "bm-null");
            await Assert.That(defaultVisible).DoesNotContain(x => x.Id == "bm-named");
        }
    }

    [Test]
    public async Task DeadLetterOriginalQueueItemIdIsUniqueAndAddOrGetIsIdempotent()
    {
        await using var scenario = await CreateScenarioAsync();
        var first = DeadLetter("dl-1", "queue-1");

        await scenario.DeadLetters.SaveAsync(first);
        await scenario.AssertUniquenessConflictAsync(() => scenario.DeadLetters.SaveAsync(DeadLetter("dl-2", "queue-1")));

        var afterConflict = (await scenario.DeadLetters.FindManyAsync(new BookmarkQueueDeadLetterFilter { OriginalQueueItemId = "queue-1" })).ToList();
        await Assert.That((await Assert.That(afterConflict).HasSingleItem()).Id).IsEqualTo("dl-1");

        var existing = await scenario.DeadLetters.AddOrGetExistingAsync(DeadLetter("dl-3", "queue-1"));
        await Assert.That(existing.Id).IsEqualTo("dl-1");

        var created = await scenario.DeadLetters.AddOrGetExistingAsync(DeadLetter("dl-4", "queue-2"));
        await Assert.That(created.Id).IsEqualTo("dl-4");

        var all = (await scenario.DeadLetters.FindManyAsync(new BookmarkQueueDeadLetterFilter())).ToList();
        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(all).Contains(x => x.Id == "dl-1");
        await Assert.That(all).Contains(x => x.Id == "dl-4");
    }

    [Test]
    public async Task BookmarkActivityExecutionAndExecutionLogFindsHonorFiltersAndIdUpsert()
    {
        await using var scenario = await CreateScenarioAsync();

        await scenario.Bookmarks.SaveAsync(Bookmark("bm-1", hash: "hash-http", workflowInstanceId: "instance-1"));
        await scenario.Bookmarks.SaveAsync(Bookmark("bm-2", hash: "hash-timer", workflowInstanceId: "instance-1"));
        await scenario.Bookmarks.SaveAsync(Bookmark("bm-3", hash: "hash-http", workflowInstanceId: "instance-2"));

        await Assert.That((await scenario.Bookmarks.FindAsync(new BookmarkFilter { BookmarkId = "bm-1" }))!.Id).IsEqualTo("bm-1");
        await Assert.That((await scenario.Bookmarks.FindAsync(new BookmarkFilter { Hash = "hash-timer" }))!.Id).IsEqualTo("bm-2");
        await Assert.That(await scenario.Bookmarks.FindAsync(new BookmarkFilter { BookmarkId = "missing" })).IsNull();

        var instanceBookmarks = (await scenario.Bookmarks.FindManyAsync(new BookmarkFilter { WorkflowInstanceId = "instance-1" })).ToList();
        await Assert.That(instanceBookmarks.Count).IsEqualTo(2);
        await Assert.That(instanceBookmarks).Contains(x => x.Id == "bm-1");
        await Assert.That(instanceBookmarks).Contains(x => x.Id == "bm-2");

        var hashed = (await scenario.Bookmarks.FindManyAsync(new BookmarkFilter { Hash = "hash-http" })).ToList();
        await Assert.That(hashed.Count).IsEqualTo(2);

        var firstPage = await scenario.Bookmarks.FindManyAsync(new BookmarkFilter { WorkflowInstanceId = "instance-1" }, PageArgs.FromRange(0, 1));
        await Assert.That(firstPage.TotalCount).IsEqualTo(2);
        await Assert.That(firstPage.Items).HasSingleItem();

        await scenario.Bookmarks.SaveAsync(Bookmark("bm-1", hash: "hash-updated", workflowInstanceId: "instance-1"));
        await Assert.That((await scenario.Bookmarks.FindAsync(new BookmarkFilter { BookmarkId = "bm-1" }))!.Hash).IsEqualTo("hash-updated");

        await scenario.ActivityExecutions.SaveAsync(ActivityExecution("ae-1", "instance-1", "activity-a", ActivityStatus.Running));
        await scenario.ActivityExecutions.SaveAsync(ActivityExecution("ae-2", "instance-1", "activity-b", ActivityStatus.Completed, completedAt: StartedAt.AddMinutes(1)));
        await scenario.ActivityExecutions.SaveAsync(ActivityExecution("ae-3", "instance-2", "activity-a", ActivityStatus.Running));

        await Assert.That((await scenario.ActivityExecutions.FindAsync(new ActivityExecutionRecordFilter { Id = "ae-1" }))!.Id).IsEqualTo("ae-1");
        await Assert.That(await scenario.ActivityExecutions.FindAsync(new ActivityExecutionRecordFilter { Id = "missing" })).IsNull();

        var instanceActivities = (await scenario.ActivityExecutions.FindManyAsync(new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1" })).ToList();
        await Assert.That(instanceActivities.Count).IsEqualTo(2);
        await Assert.That(await scenario.ActivityExecutions.CountAsync(new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1" })).IsEqualTo(2);
        await Assert.That((await Assert.That(await scenario.ActivityExecutions.FindManyAsync(new ActivityExecutionRecordFilter { ActivityId = "activity-a", WorkflowInstanceId = "instance-1" })).HasSingleItem()).Id).IsEqualTo("ae-1");
        await Assert.That((await Assert.That(await scenario.ActivityExecutions.FindManyAsync(new ActivityExecutionRecordFilter { Status = ActivityStatus.Completed })).HasSingleItem()).Id).IsEqualTo("ae-2");

        await scenario.ActivityExecutions.SaveAsync(ActivityExecution("ae-1", "instance-1", "activity-a", ActivityStatus.Completed, completedAt: StartedAt.AddMinutes(2)));
        await Assert.That((await scenario.ActivityExecutions.FindAsync(new ActivityExecutionRecordFilter { Id = "ae-1" }))!.Status).IsEqualTo(ActivityStatus.Completed);

        await Assert.That(await scenario.ActivityExecutions.DeleteManyAsync(new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1" })).IsEqualTo(2);
        await Assert.That((await Assert.That(await scenario.ActivityExecutions.FindManyAsync(new ActivityExecutionRecordFilter())).HasSingleItem()).Id).IsEqualTo("ae-3");

        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-1", "instance-1", "activity-a", "Started"));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-2", "instance-1", "activity-b", "Completed", activityType: "Elsa.Delay"));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-3", "instance-2", "activity-a", "Started"));

        await Assert.That((await scenario.ExecutionLogs.FindAsync(new WorkflowExecutionLogRecordFilter { Id = "el-1" }))!.Id).IsEqualTo("el-1");
        await Assert.That(await scenario.ExecutionLogs.FindAsync(new WorkflowExecutionLogRecordFilter { Id = "missing" })).IsNull();

        var instanceLogs = await scenario.ExecutionLogs.FindManyAsync(new WorkflowExecutionLogRecordFilter { WorkflowInstanceId = "instance-1" }, PageArgs.FromRange(0, 10));
        await Assert.That(instanceLogs.TotalCount).IsEqualTo(2);
        await Assert.That(instanceLogs.Items.Count).IsEqualTo(2);

        var firstLogPage = await scenario.ExecutionLogs.FindManyAsync(new WorkflowExecutionLogRecordFilter { WorkflowInstanceId = "instance-1" }, PageArgs.FromRange(0, 1));
        await Assert.That(firstLogPage.TotalCount).IsEqualTo(2);
        await Assert.That(firstLogPage.Items).HasSingleItem();

        await Assert.That((await scenario.ExecutionLogs.FindAsync(new WorkflowExecutionLogRecordFilter { EventName = "Completed" }))!.Id).IsEqualTo("el-2");
        await Assert.That((await Assert.That((await scenario.ExecutionLogs.FindManyAsync(new WorkflowExecutionLogRecordFilter { ActivityId = "activity-b" }, PageArgs.All)).Items).HasSingleItem()).Id).IsEqualTo("el-2");

        var excluded = await scenario.ExecutionLogs.FindManyAsync(new WorkflowExecutionLogRecordFilter { WorkflowInstanceId = "instance-1", ExcludeActivityType = "Elsa.WriteLine" }, PageArgs.All);
        await Assert.That((await Assert.That(excluded.Items).HasSingleItem()).Id).IsEqualTo("el-2");

        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-1", "instance-1", "activity-a", "Resumed"));
        await Assert.That((await scenario.ExecutionLogs.FindAsync(new WorkflowExecutionLogRecordFilter { Id = "el-1" }))!.EventName).IsEqualTo("Resumed");

        await Assert.That(await scenario.ExecutionLogs.DeleteManyAsync(new WorkflowExecutionLogRecordFilter { WorkflowInstanceId = "instance-1" })).IsEqualTo(2);
        await Assert.That((await Assert.That((await scenario.ExecutionLogs.FindManyAsync(new WorkflowExecutionLogRecordFilter(), PageArgs.All)).Items).HasSingleItem()).Id).IsEqualTo("el-3");
    }

    [Test]
    public async Task ExecutionLogDefaultOrderIsTimestampThenSequenceAndLastEntryUsesSequence()
    {
        await using var scenario = await CreateScenarioAsync();
        var sameTimestamp = StartedAt;
        var laterTimestamp = StartedAt.AddMinutes(1);

        // Insert later Sequence first so dictionary/heap order cannot accidentally match the contract.
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-seq-2", "instance-1", "activity-a", "Completed", timestamp: sameTimestamp, sequence: 2));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-seq-3", "instance-1", "activity-a", "Faulted", timestamp: sameTimestamp, sequence: 3));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-seq-1", "instance-1", "activity-a", "Started", timestamp: sameTimestamp, sequence: 1));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-later", "instance-1", "activity-b", "Started", timestamp: laterTimestamp, sequence: 0));

        var filter = new WorkflowExecutionLogRecordFilter { WorkflowInstanceId = "instance-1" };
        var page = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.All);
        await Assert.That(page.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-seq-1", "el-seq-2", "el-seq-3", "el-later"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var firstPage = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.FromRange(0, 2));
        await Assert.That(firstPage.TotalCount).IsEqualTo(4);
        await Assert.That(firstPage.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-seq-1", "el-seq-2"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var first = await scenario.ExecutionLogs.FindAsync(filter);
        await Assert.That(first!.Id).IsEqualTo("el-seq-1");

        var sameTimestampFilter = new WorkflowExecutionLogRecordFilter
        {
            WorkflowInstanceId = "instance-1",
            ActivityId = "activity-a",
            EventNames = ["Started", "Completed", "Faulted"]
        };
        var lastEntryOrder = new WorkflowExecutionLogRecordOrder<long>(x => x.Sequence, OrderDirection.Descending);
        var last = await scenario.ExecutionLogs.FindAsync(sameTimestampFilter, lastEntryOrder);
        await Assert.That(last!.Id).IsEqualTo("el-seq-3");
    }

    [Test]
    public async Task ExecutionLogDefaultOrderUsesIdWhenTimestampAndSequenceTieAcrossInstances()
    {
        await using var scenario = await CreateScenarioAsync();
        var timestamp = StartedAt;
        const long sequence = 1;

        // Reverse Id insert order so dictionary/heap order cannot accidentally match Id ascending.
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-d", "instance-4", "activity-a", "Started", timestamp: timestamp, sequence: sequence));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-b", "instance-2", "activity-a", "Started", timestamp: timestamp, sequence: sequence));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-c", "instance-3", "activity-a", "Started", timestamp: timestamp, sequence: sequence));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-a", "instance-1", "activity-a", "Started", timestamp: timestamp, sequence: sequence));

        var filter = new WorkflowExecutionLogRecordFilter();
        var all = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.All);
        await Assert.That(all.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-a", "el-b", "el-c", "el-d"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var firstPage = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.FromRange(0, 2));
        var secondPage = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.FromRange(2, 2));
        await Assert.That(firstPage.TotalCount).IsEqualTo(4);
        await Assert.That(firstPage.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-a", "el-b"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(secondPage.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-c", "el-d"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExecutionLogOrderedFindManyUsesProvidedOrderNotDefault()
    {
        await using var scenario = await CreateScenarioAsync();
        var timestamp = StartedAt;

        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-seq-2", "instance-1", "activity-a", "Completed", timestamp: timestamp, sequence: 2));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-seq-3", "instance-1", "activity-a", "Faulted", timestamp: timestamp, sequence: 3));
        await scenario.ExecutionLogs.SaveAsync(ExecutionLog("el-seq-1", "instance-1", "activity-a", "Started", timestamp: timestamp, sequence: 1));

        var filter = new WorkflowExecutionLogRecordFilter
        {
            WorkflowInstanceId = "instance-1",
            ActivityId = "activity-a"
        };

        var defaultPage = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.All);
        await Assert.That(defaultPage.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-seq-1", "el-seq-2", "el-seq-3"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var descending = new WorkflowExecutionLogRecordOrder<long>(x => x.Sequence, OrderDirection.Descending);
        var descendingPage = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.All, descending);
        await Assert.That(descendingPage.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-seq-3", "el-seq-2", "el-seq-1"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var firstDescendingPage = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.FromRange(0, 2), descending);
        await Assert.That(firstDescendingPage.TotalCount).IsEqualTo(3);
        await Assert.That(firstDescendingPage.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-seq-3", "el-seq-2"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var journalOrder = new WorkflowExecutionLogRecordOrder<long>(x => x.Sequence, OrderDirection.Ascending);
        var journalPage = await scenario.ExecutionLogs.FindManyAsync(filter, PageArgs.FromRange(0, 2), journalOrder);
        await Assert.That(journalPage.Items.Select(x => x.Id).ToArray()).IsEquivalentTo(["el-seq-1", "el-seq-2"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ActivityExecutionFindManyAndSummariesShareCompletedStatusAndNameFilters()
    {
        await using var scenario = await CreateScenarioAsync();

        await scenario.ActivityExecutions.SaveAsync(ActivityExecution("ae-running", "instance-1", "activity-a", ActivityStatus.Running, activityName: "WriteLine"));
        await scenario.ActivityExecutions.SaveAsync(ActivityExecution(
            "ae-completed",
            "instance-1",
            "activity-b",
            ActivityStatus.Completed,
            completedAt: StartedAt.AddMinutes(1),
            activityName: "WriteLine",
            startedAt: StartedAt.AddMinutes(1)));
        await scenario.ActivityExecutions.SaveAsync(ActivityExecution(
            "ae-faulted",
            "instance-1",
            "activity-c",
            ActivityStatus.Faulted,
            completedAt: StartedAt.AddMinutes(2),
            activityName: "HttpEndpoint",
            startedAt: StartedAt.AddMinutes(2)));
        await scenario.ActivityExecutions.SaveAsync(ActivityExecution(
            "ae-other",
            "instance-2",
            "activity-b",
            ActivityStatus.Completed,
            completedAt: StartedAt.AddMinutes(1),
            activityName: "WriteLine"));

        await AssertFindManyAndSummariesMatchAsync(scenario, new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1", Completed = true }, ["ae-completed", "ae-faulted"]);
        await AssertFindManyAndSummariesMatchAsync(scenario, new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1", Completed = false }, ["ae-running"]);
        await AssertFindManyAndSummariesMatchAsync(scenario, new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1", Status = ActivityStatus.Completed }, ["ae-completed"]);
        await AssertFindManyAndSummariesMatchAsync(scenario, new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1", Name = "HttpEndpoint" }, ["ae-faulted"]);
        await AssertFindManyAndSummariesMatchAsync(scenario, new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1", Name = "WriteLine" }, ["ae-running", "ae-completed"]);
    }

    [Test]
    public async Task ActivityExecutionSummariesHonorStartedAtOrderAndProjectIdentityFields()
    {
        await using var scenario = await CreateScenarioAsync();

        await scenario.ActivityExecutions.SaveAsync(ActivityExecution(
            "ae-late",
            "instance-1",
            "activity-c",
            ActivityStatus.Completed,
            completedAt: StartedAt.AddMinutes(3),
            activityName: "WriteLine",
            startedAt: StartedAt.AddMinutes(2)));
        await scenario.ActivityExecutions.SaveAsync(ActivityExecution(
            "ae-early",
            "instance-1",
            "activity-a",
            ActivityStatus.Running,
            activityName: "HttpEndpoint"));
        await scenario.ActivityExecutions.SaveAsync(ActivityExecution(
            "ae-mid",
            "instance-1",
            "activity-b",
            ActivityStatus.Faulted,
            completedAt: StartedAt.AddMinutes(2),
            activityName: "WriteLine",
            startedAt: StartedAt.AddMinutes(1)));

        var filter = new ActivityExecutionRecordFilter { WorkflowInstanceId = "instance-1" };
        var order = new ActivityExecutionRecordOrder<DateTimeOffset>(x => x.StartedAt, OrderDirection.Ascending);
        var records = (await scenario.ActivityExecutions.FindManyAsync(filter, order)).ToList();
        var summaries = (await scenario.ActivityExecutions.FindManySummariesAsync(filter, order)).ToList();

        await Assert.That(records.Select(x => x.Id).ToArray()).IsEquivalentTo(["ae-early", "ae-mid", "ae-late"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(summaries.Select(x => x.Id).ToArray()).IsEquivalentTo(["ae-early", "ae-mid", "ae-late"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        for (var i = 0; i < records.Count; i++)
        {
            await Assert.That(summaries[i].Id).IsEqualTo(records[i].Id);
            await Assert.That(summaries[i].WorkflowInstanceId).IsEqualTo(records[i].WorkflowInstanceId);
            await Assert.That(summaries[i].ActivityId).IsEqualTo(records[i].ActivityId);
            await Assert.That(summaries[i].ActivityName).IsEqualTo(records[i].ActivityName);
            await Assert.That(summaries[i].Status).IsEqualTo(records[i].Status);
            await Assert.That(summaries[i].StartedAt).IsEqualTo(records[i].StartedAt);
            await Assert.That(summaries[i].CompletedAt).IsEqualTo(records[i].CompletedAt);
        }
    }

    private static async Task SeedMixedTriggersAsync(WorkflowStoreScenario scenario)
    {
        await scenario.Triggers.SaveAsync(Trigger("id-a", hash: "hash-a", tenantId: "tenant-a"));
        await scenario.Triggers.SaveAsync(Trigger("id-b", hash: "hash-b", tenantId: "tenant-b"));
        await scenario.Triggers.SaveAsync(Trigger("id-star", hash: "hash-star", tenantId: Tenant.AgnosticTenantId));
    }

    private static async Task SeedMixedDefinitionsAsync(WorkflowStoreScenario scenario)
    {
        await scenario.Definitions.SaveAsync(Definition("def-a", "A", "tenant-a"));
        await scenario.Definitions.SaveAsync(Definition("def-b", "B", "tenant-b"));
        await scenario.Definitions.SaveAsync(Definition("def-star", "Star", Tenant.AgnosticTenantId));
    }

    private static async Task SeedMixedBookmarksAsync(WorkflowStoreScenario scenario)
    {
        await scenario.Bookmarks.SaveAsync(Bookmark("bm-a", tenantId: "tenant-a"));
        await scenario.Bookmarks.SaveAsync(Bookmark("bm-b", tenantId: "tenant-b"));
        await scenario.Bookmarks.SaveAsync(Bookmark("bm-star", tenantId: Tenant.AgnosticTenantId));
    }

    private static StoredTrigger Trigger(
        string id,
        string workflowDefinitionId = "workflow-1",
        string workflowDefinitionVersionId = "v1",
        string activityId = "activity-1",
        string? hash = "hash-1",
        string? tenantId = "tenant-a") =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            WorkflowDefinitionId = workflowDefinitionId,
            WorkflowDefinitionVersionId = workflowDefinitionVersionId,
            ActivityId = activityId,
            Hash = hash,
            Name = "Elsa.HttpEndpoint"
        };

    private static WorkflowDefinition Definition(string id, string name, string? tenantId, string? definitionId = null, int version = 1) =>
        new()
        {
            Id = id,
            DefinitionId = definitionId ?? name.ToLowerInvariant(),
            Name = name,
            TenantId = tenantId,
            Version = version,
            IsLatest = version == 1,
            MaterializerName = "Json",
            CreatedAt = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)
        };

    private static readonly DateTimeOffset StartedAt = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static StoredBookmark Bookmark(
        string id,
        string? tenantId = "tenant-a",
        string? hash = null,
        string workflowInstanceId = "instance-1") =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Hash = hash ?? id,
            WorkflowInstanceId = workflowInstanceId,
            Name = "Elsa.HttpEndpoint",
            CreatedAt = StartedAt
        };

    private static async Task AssertFindManyAndSummariesMatchAsync(
        WorkflowStoreScenario scenario,
        ActivityExecutionRecordFilter filter,
        string[] expectedIds)
    {
        var records = (await scenario.ActivityExecutions.FindManyAsync(filter)).Select(x => x.Id).OrderBy(x => x).ToArray();
        var summaries = (await scenario.ActivityExecutions.FindManySummariesAsync(filter)).Select(x => x.Id).OrderBy(x => x).ToArray();
        var expected = expectedIds.OrderBy(x => x).ToArray();

        await Assert.That(records).IsEquivalentTo(expected, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(summaries).IsEquivalentTo(expected, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static ActivityExecutionRecord ActivityExecution(
        string id,
        string workflowInstanceId,
        string activityId,
        ActivityStatus status,
        DateTimeOffset? completedAt = null,
        string? activityName = null,
        DateTimeOffset? startedAt = null) =>
        new()
        {
            Id = id,
            TenantId = "tenant-a",
            WorkflowInstanceId = workflowInstanceId,
            ActivityId = activityId,
            ActivityNodeId = $"node-{activityId}",
            ActivityType = "Elsa.WriteLine",
            ActivityTypeVersion = 1,
            ActivityName = activityName ?? activityId,
            Status = status,
            StartedAt = startedAt ?? StartedAt,
            CompletedAt = completedAt
        };

    private static WorkflowExecutionLogRecord ExecutionLog(
        string id,
        string workflowInstanceId,
        string activityId,
        string eventName,
        string activityType = "Elsa.WriteLine",
        DateTimeOffset? timestamp = null,
        long sequence = 0) =>
        new()
        {
            Id = id,
            TenantId = "tenant-a",
            WorkflowDefinitionId = "workflow-1",
            WorkflowDefinitionVersionId = "v1",
            WorkflowInstanceId = workflowInstanceId,
            WorkflowVersion = 1,
            ActivityInstanceId = $"ai-{id}",
            ActivityId = activityId,
            ActivityType = activityType,
            ActivityTypeVersion = 1,
            ActivityNodeId = $"node-{activityId}",
            Timestamp = timestamp ?? StartedAt,
            Sequence = sequence,
            EventName = eventName
        };

    private static BookmarkQueueDeadLetterItem DeadLetter(string id, string originalQueueItemId) =>
        new()
        {
            Id = id,
            TenantId = "tenant-a",
            OriginalQueueItemId = originalQueueItemId,
            WorkflowInstanceId = "instance-1",
            Reason = "delivery-failed",
            OriginalCreatedAt = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            DeadLetteredAt = new DateTimeOffset(2026, 9, 13, 12, 5, 0, TimeSpan.Zero)
        };
}

[InheritsTests]
public sealed class InMemoryWorkflowStoreConformanceTests : WorkflowStoreConformanceTests
{
    protected override Task<WorkflowStoreScenario> CreateScenarioAsync() => WorkflowStoreScenario.CreateInMemoryAsync();
}

[InheritsTests]
public sealed class PostgreSqlWorkflowStoreConformanceTests : WorkflowStoreConformanceTests
{
    protected override Task<WorkflowStoreScenario> CreateScenarioAsync() => WorkflowStoreScenario.CreatePostgreSqlAsync();
}
