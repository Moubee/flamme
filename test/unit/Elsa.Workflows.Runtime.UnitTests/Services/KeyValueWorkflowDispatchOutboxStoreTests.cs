using System.Text.Json;
using Elsa.Common.Services;
using Elsa.KeyValues.Contracts;
using Elsa.KeyValues.Entities;
using Elsa.KeyValues.Models;
using Elsa.KeyValues.Stores;
using Elsa.Testing.Shared.Multitenancy;
using Elsa.Workflows.Runtime.Commands;
using Elsa.Workflows.Runtime.Models;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class KeyValueWorkflowDispatchOutboxStoreTests
{
    private readonly IKeyValueStore _keyValueStore = Mock.Of<IKeyValueStore>().Object;
    private readonly TestPayloadSerializer _payloadSerializer = new();
    private readonly KeyValueWorkflowDispatchOutboxStore _store;

    public KeyValueWorkflowDispatchOutboxStoreTests()
    {
        _store = new(_keyValueStore, _payloadSerializer);
        Mock.Get(_keyValueStore).FindAsync(Arg.Any<KeyValueFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() => null);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Any<KeyValueFilter>(), Arg.Any<CancellationToken>())
                    .Returns(Array.Empty<SerializedKeyValuePair>());
    }

    [Test]
    public async Task FindManyAsync_ReturnsOldestItemsBeforeApplyingLimit()
    {
        var newest = CreateItem("newest", new DateTimeOffset(2026, 5, 20, 12, 2, 0, TimeSpan.Zero));
        var oldest = CreateItem("oldest", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var middle = CreateItem("middle", new DateTimeOffset(2026, 5, 20, 12, 1, 0, TimeSpan.Zero));
        _payloadSerializer.Items = new[] { newest, oldest, middle }.ToDictionary(x => x.Id);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:"), Arg.Any<CancellationToken>())
                    .Returns(new[] { oldest, middle }.Select(x => new SerializedKeyValuePair { Key = $"index:{x.Id}", SerializedValue = x.Id }));
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Keys != null), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.KeyValues.Models.KeyValueFilter __arg0, global::System.Threading.CancellationToken __arg1) => ((KeyValueFilter)__arg0).Keys!.Select(x => new SerializedKeyValuePair { Key = x, SerializedValue = x.Split(':').Last() }));

        var result = (await _store.FindManyAsync(2)).ToList();

        await Assert.That(result.Select(x => x.Id)).IsEquivalentTo(["oldest", "middle"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        Mock.Get(_keyValueStore).FindManyAsync(
            Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:" && x.OrderByKey && x.Take == 2), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindManyAsync_MergesIndexedLegacyAndOrphanItems()
    {
        var indexed = CreateItem("indexed", new DateTimeOffset(2026, 5, 20, 12, 2, 0, TimeSpan.Zero));
        var legacy = CreateItem("legacy", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var orphan = CreateItem("orphan", new DateTimeOffset(2026, 5, 20, 12, 1, 0, TimeSpan.Zero));
        _payloadSerializer.Items = new[] { indexed, legacy, orphan }.ToDictionary(x => x.Id);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:"), Arg.Any<CancellationToken>())
                    .Returns([CreateIndexRecord(indexed)]);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Keys != null), Arg.Any<CancellationToken>())
                    .Returns([CreateItemRecord(indexed)]);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateItemRecord(indexed), CreateLegacyRecord(legacy), CreateItemRecord(orphan), CreateIndexRecord(indexed)]);

        var result = (await _store.FindManyAsync()).ToList();

        await Assert.That(result.Select(x => x.Id)).IsEquivalentTo(["legacy", "orphan", "indexed"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task FindManyAsync_AppliesLimitAfterMergingRecoverableItems()
    {
        var indexed1 = CreateItem("indexed-1", new DateTimeOffset(2026, 5, 20, 12, 1, 0, TimeSpan.Zero));
        var indexed2 = CreateItem("indexed-2", new DateTimeOffset(2026, 5, 20, 12, 2, 0, TimeSpan.Zero));
        var orphan = CreateItem("orphan", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        _payloadSerializer.Items = new[] { indexed1, indexed2, orphan }.ToDictionary(x => x.Id);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:"), Arg.Any<CancellationToken>())
                    .Returns([CreateIndexRecord(indexed1), CreateIndexRecord(indexed2)]);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Keys != null), Arg.Any<CancellationToken>())
                    .Returns([CreateItemRecord(indexed1), CreateItemRecord(indexed2)]);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateItemRecord(orphan)]);

        var result = (await _store.FindManyAsync(2)).ToList();

        await Assert.That(result.Select(x => x.Id)).IsEquivalentTo(["orphan", "indexed-1"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task FindManyAsync_MigratesOnlyCallerLimitFromLegacyScan()
    {
        var first = CreateItem("legacy-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var second = CreateItem("legacy-2", new DateTimeOffset(2026, 5, 20, 12, 1, 0, TimeSpan.Zero));
        var third = CreateItem("legacy-3", new DateTimeOffset(2026, 5, 20, 12, 2, 0, TimeSpan.Zero));
        _payloadSerializer.Items = new[] { first, second, third }.ToDictionary(x => x.Id);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateLegacyRecord(first), CreateLegacyRecord(second), CreateLegacyRecord(third)]);

        var result = (await _store.FindManyAsync(2)).ToList();

        await Assert.That(result.Select(x => x.Id)).IsEquivalentTo(["legacy-1", "legacy-2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:legacy-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:legacy-2"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:legacy-3"), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == DefaultLegacyScanCompletedKey), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_keyValueStore).FindManyAsync(
            Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:" && x.StartsWith && x.OrderByKey && x.Take == 3), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindManyAsync_MarksLegacyScanCompleted_WhenCallerLimitCoversRecoverableItems()
    {
        var first = CreateItem("legacy-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var second = CreateItem("legacy-2", new DateTimeOffset(2026, 5, 20, 12, 1, 0, TimeSpan.Zero));
        _payloadSerializer.Items = new[] { first, second }.ToDictionary(x => x.Id);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateLegacyRecord(first), CreateLegacyRecord(second)]);

        var result = (await _store.FindManyAsync(2)).ToList();

        await Assert.That(result.Select(x => x.Id)).IsEquivalentTo(["legacy-1", "legacy-2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == DefaultLegacyScanCompletedKey && x.SerializedValue == "true"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindManyAsync_UsesRecoveryMarkersWithoutBroadScan_WhenLegacyScanAlreadyCompleted()
    {
        var orphan = CreateItem("orphan", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        _payloadSerializer.Items[orphan.Id] = orphan;
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == DefaultLegacyScanCompletedKey), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = DefaultLegacyScanCompletedKey, SerializedValue = "true" });
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Recovery:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateRecoveryRecord(orphan)]);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Keys != null), Arg.Any<CancellationToken>())
                    .Returns([CreateItemRecord(orphan)]);

        var result = (await _store.FindManyAsync()).ToList();

        await Assert.That(result.Select(x => x.Id)).IsEquivalentTo(["orphan"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        Mock.Get(_keyValueStore).FindManyAsync(
            Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:" && x.StartsWith), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == $"Elsa:WorkflowDispatchOutbox:Index:{orphan.CreatedAt.UtcTicks:D20}:orphan" && x.SerializedValue == "orphan"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:Recovery:orphan", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindManyAsync_AppliesCallerLimitToRecoveryScan()
    {
        var first = CreateItem("orphan-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var second = CreateItem("orphan-2", new DateTimeOffset(2026, 5, 20, 12, 1, 0, TimeSpan.Zero));
        _payloadSerializer.Items = new[] { first, second }.ToDictionary(x => x.Id);
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == DefaultLegacyScanCompletedKey), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = DefaultLegacyScanCompletedKey, SerializedValue = "true" });
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Recovery:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateRecoveryRecord(first), CreateRecoveryRecord(second)]);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Keys != null), Arg.Any<CancellationToken>())
                    .Returns([CreateItemRecord(first), CreateItemRecord(second)]);

        var result = (await _store.FindManyAsync(2)).ToList();

        await Assert.That(result.Select(x => x.Id)).IsEquivalentTo(["orphan-1", "orphan-2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        Mock.Get(_keyValueStore).FindManyAsync(
            Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Recovery:" && x.StartsWith && x.OrderByKey && x.Take == 2), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindManyAsync_MarksLegacyScanCompletedIndependentlyPerTenant()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        var tenantAKv = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-a"));
        var tenantBKv = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-b"));
        var payloadSerializer = new TestPayloadSerializer();
        var tenantAStore = new KeyValueWorkflowDispatchOutboxStore(tenantAKv, payloadSerializer, new TestTenantAccessor("tenant-a"));
        var tenantBStore = new KeyValueWorkflowDispatchOutboxStore(tenantBKv, payloadSerializer, new TestTenantAccessor("tenant-b"));
        var tenantBLegacy = CreateItem("legacy-b", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        tenantBLegacy.TenantId = "tenant-b";
        payloadSerializer.Items[tenantBLegacy.Id] = tenantBLegacy;
        await tenantBKv.SaveAsync(new SerializedKeyValuePair
        {
            Key = $"Elsa:WorkflowDispatchOutbox:{tenantBLegacy.Id}",
            SerializedValue = tenantBLegacy.Id,
            TenantId = "tenant-b"
        }, CancellationToken.None);

        var tenantAFirst = (await tenantAStore.FindManyAsync()).ToList();
        var tenantBResult = (await tenantBStore.FindManyAsync()).ToList();
        var tenantASecond = (await tenantAStore.FindManyAsync()).ToList();

        await Assert.That(tenantAFirst).IsEmpty();
        await Assert.That(tenantBResult.Select(x => x.Id)).IsEquivalentTo(["legacy-b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tenantASecond).IsEmpty();
        await Assert.That(await tenantAKv.FindAsync(new KeyValueFilter { Key = tenantAStore.GetLegacyScanCompletedKey() }, CancellationToken.None)).IsNotNull();
        await Assert.That(await tenantBKv.FindAsync(new KeyValueFilter { Key = tenantBStore.GetLegacyScanCompletedKey() }, CancellationToken.None)).IsNotNull();
        await Assert.That(await tenantAKv.FindAsync(new KeyValueFilter { Key = tenantBStore.GetLegacyScanCompletedKey() }, CancellationToken.None)).IsNull();
        await Assert.That(tenantBStore.GetLegacyScanCompletedKey()).IsNotEqualTo(tenantAStore.GetLegacyScanCompletedKey());
    }

    [Test]
    public async Task FindManyAsync_MarksLegacyScanCompletedIndependentlyForEmptyAndNamedDefaultTenants()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        var emptyTenantKv = new MemoryKeyValueStore(backing, new TestTenantAccessor(""));
        var namedDefaultKv = new MemoryKeyValueStore(backing, new TestTenantAccessor("default"));
        var payloadSerializer = new TestPayloadSerializer();
        var emptyStore = new KeyValueWorkflowDispatchOutboxStore(emptyTenantKv, payloadSerializer, new TestTenantAccessor(""));
        var namedDefaultStore = new KeyValueWorkflowDispatchOutboxStore(namedDefaultKv, payloadSerializer, new TestTenantAccessor("default"));

        await emptyStore.FindManyAsync();
        await namedDefaultStore.FindManyAsync();

        await Assert.That(emptyStore.GetLegacyScanCompletedKey()).IsEqualTo("Elsa:WorkflowDispatchOutbox:State:LegacyScanCompleted:");
        await Assert.That(namedDefaultStore.GetLegacyScanCompletedKey()).IsEqualTo("Elsa:WorkflowDispatchOutbox:State:LegacyScanCompleted:default");
        await Assert.That(await emptyTenantKv.FindAsync(new KeyValueFilter { Key = emptyStore.GetLegacyScanCompletedKey() }, CancellationToken.None)).IsNotNull();
        await Assert.That(await namedDefaultKv.FindAsync(new KeyValueFilter { Key = namedDefaultStore.GetLegacyScanCompletedKey() }, CancellationToken.None)).IsNotNull();
        await Assert.That(await emptyTenantKv.FindAsync(new KeyValueFilter { Key = namedDefaultStore.GetLegacyScanCompletedKey() }, CancellationToken.None)).IsNull();
        await Assert.That(await namedDefaultKv.FindAsync(new KeyValueFilter { Key = emptyStore.GetLegacyScanCompletedKey() }, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task FindManyAsync_CleansUpCorruptIndexedItem_AllowingLaterItemsToProgress()
    {
        var keyValueStore = new MemoryKeyValueStore(new MemoryStore<SerializedKeyValuePair>());
        var payloadSerializer = new TestPayloadSerializer();
        var store = new KeyValueWorkflowDispatchOutboxStore(keyValueStore, payloadSerializer);
        var corrupt = CreateItem("corrupt", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var valid = CreateItem("valid", new DateTimeOffset(2026, 5, 20, 12, 1, 0, TimeSpan.Zero));
        payloadSerializer.Items[valid.Id] = valid;
        await SaveAsync(keyValueStore, CreateItemRecord(corrupt));
        await SaveAsync(keyValueStore, CreateIndexRecord(corrupt));
        await SaveAsync(keyValueStore, CreateIndexByIdRecord(corrupt));
        await SaveAsync(keyValueStore, CreateRecoveryRecord(corrupt));
        await SaveAsync(keyValueStore, CreateItemRecord(valid));
        await SaveAsync(keyValueStore, CreateIndexRecord(valid));
        await SaveAsync(keyValueStore, CreateIndexByIdRecord(valid));
        await SaveAsync(keyValueStore, CreateLegacyScanCompletedRecord());

        var firstBatch = (await store.FindManyAsync(1)).ToList();
        var secondBatch = (await store.FindManyAsync(1)).ToList();

        await Assert.That(firstBatch).IsEmpty();
        await Assert.That(secondBatch.Select(x => x.Id)).IsEquivalentTo(["valid"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(await FindAsync(keyValueStore, $"Elsa:WorkflowDispatchOutbox:Items:{corrupt.Id}")).IsNull();
        await Assert.That(await FindAsync(keyValueStore, $"Elsa:WorkflowDispatchOutbox:Index:{corrupt.CreatedAt.UtcTicks:D20}:{corrupt.Id}")).IsNull();
        await Assert.That(await FindAsync(keyValueStore, $"Elsa:WorkflowDispatchOutbox:IndexById:{corrupt.Id}")).IsNull();
        await Assert.That(await FindAsync(keyValueStore, $"Elsa:WorkflowDispatchOutbox:Recovery:{corrupt.Id}")).IsNull();
    }

    [Test]
    public async Task FindManyAsync_CleansUpRecoveryRecord_WhenPayloadCannotBeDeserialized()
    {
        var item = CreateItem("corrupt", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var indexKey = $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:{item.Id}";
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Recovery:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateRecoveryRecord(item)]);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Keys != null), Arg.Any<CancellationToken>())
                    .Returns([CreateItemRecord(item)]);
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == $"Elsa:WorkflowDispatchOutbox:IndexById:{item.Id}"), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = $"Elsa:WorkflowDispatchOutbox:IndexById:{item.Id}", SerializedValue = indexKey });

        var result = (await _store.FindManyAsync()).ToList();

        await Assert.That(result).IsEmpty();
        Mock.Get(_keyValueStore).DeleteAsync(indexKey, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:IndexById:{item.Id}", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Recovery:{item.Id}", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Items:{item.Id}", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task SaveAsync_WritesItemAndSortableIndex()
    {
        var item = CreateItem("outbox-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        item.TenantId = "tenant-1";

        await _store.SaveAsync(item);

        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Recovery:outbox-1" && x.SerializedValue == "outbox-1" && x.TenantId == "tenant-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:outbox-1" && x.SerializedValue == "outbox-1" && x.TenantId == "tenant-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1" && x.SerializedValue == "outbox-1" && x.TenantId == "tenant-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).SaveAsync(
            Arg.Is<SerializedKeyValuePair>(x => x.Key == "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1" && x.SerializedValue == $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1" && x.TenantId == "tenant-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:Recovery:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DeleteAsync_RemovesIndexBeforeItem()
    {
        var item = CreateItem("outbox-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        _payloadSerializer.Items[item.Id] = item;
        Mock.Get(_keyValueStore).FindAsync(Arg.Any<KeyValueFilter>(), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = "Elsa:WorkflowDispatchOutbox:Items:outbox-1", SerializedValue = item.Id });

        await _store.DeleteAsync(item.Id);
        Mock.VerifyInOrder(() =>
                {
                    Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1", Arg.Any<CancellationToken>()).WasCalled();
                    Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:Items:outbox-1", Arg.Any<CancellationToken>()).WasCalled();
                });
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:Items:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:Recovery:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DeleteAsync_CleansUpIndexRecords_WhenItemRecordIsMissing()
    {
        var item = CreateItem("outbox-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(() => null);
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(() => null);
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateIndexRecord(item), CreateIndexRecord(CreateItem("outbox-2", item.CreatedAt))]);

        await _store.DeleteAsync(item.Id);

        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-2", Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:Items:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DeleteAsync_CleansUpIndexRecords_WhenItemPayloadCannotBeDeserialized()
    {
        var item = CreateItem("outbox-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var indexKey = $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1";
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = "Elsa:WorkflowDispatchOutbox:Items:outbox-1", SerializedValue = "invalid" });
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", SerializedValue = indexKey });

        await _store.DeleteAsync(item.Id);

        Mock.Get(_keyValueStore).DeleteAsync(indexKey, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:Items:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DeleteAsync_UsesIndexByIdBeforeScanningAllIndexes()
    {
        var item = CreateItem("outbox-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var indexKey = $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1";
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(() => null);
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", SerializedValue = indexKey });

        await _store.DeleteAsync(item.Id);

        Mock.Get(_keyValueStore).DeleteAsync(indexKey, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).FindManyAsync(
            Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:" && x.StartsWith), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task DeleteAsync_UsesScan_WhenIndexByIdValueDoesNotMatchExpectedId()
    {
        var item = CreateItem("outbox-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var mismatchedIndexKey = $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-2";
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(() => null);
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", SerializedValue = mismatchedIndexKey });
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateIndexRecord(item)]);

        await _store.DeleteAsync(item.Id);

        Mock.Get(_keyValueStore).DeleteAsync(mismatchedIndexKey, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).FindManyAsync(
            Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:" && x.StartsWith), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task DeleteAsync_UsesScan_WhenIndexByIdValueIsNotWellFormed()
    {
        var item = CreateItem("outbox-1", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Items:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(() => null);
        Mock.Get(_keyValueStore).FindAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1"), Arg.Any<CancellationToken>())
                    .Returns(new SerializedKeyValuePair { Key = "Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", SerializedValue = "arbitrary-key" });
        Mock.Get(_keyValueStore).FindManyAsync(Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:" && x.StartsWith), Arg.Any<CancellationToken>())
                    .Returns([CreateIndexRecord(item)]);

        await _store.DeleteAsync(item.Id);

        Mock.Get(_keyValueStore).DeleteAsync("arbitrary-key", Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_keyValueStore).DeleteAsync($"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).DeleteAsync("Elsa:WorkflowDispatchOutbox:IndexById:outbox-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_keyValueStore).FindManyAsync(
            Arg.Is<KeyValueFilter>(x => x.Key == "Elsa:WorkflowDispatchOutbox:Index:" && x.StartsWith), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    private static WorkflowDispatchOutboxItem CreateItem(string id, DateTimeOffset createdAt)
    {
        return new()
        {
            Id = id,
            OwnerWorkflowInstanceId = "parent-1",
            Kind = WorkflowDispatchOutboxItemKind.WorkflowDefinition,
            WorkflowDefinitionCommand = new DispatchWorkflowDefinitionCommand("definition-version-1"),
            CreatedAt = createdAt
        };
    }

    private static SerializedKeyValuePair CreateItemRecord(WorkflowDispatchOutboxItem item) => new()
    {
        Key = $"Elsa:WorkflowDispatchOutbox:Items:{item.Id}",
        SerializedValue = item.Id
    };

    private static SerializedKeyValuePair CreateLegacyRecord(WorkflowDispatchOutboxItem item) => new()
    {
        Key = $"Elsa:WorkflowDispatchOutbox:{item.Id}",
        SerializedValue = item.Id
    };

    private static SerializedKeyValuePair CreateIndexRecord(WorkflowDispatchOutboxItem item) => new()
    {
        Key = $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:{item.Id}",
        SerializedValue = item.Id
    };

    private static SerializedKeyValuePair CreateIndexByIdRecord(WorkflowDispatchOutboxItem item) => new()
    {
        Key = $"Elsa:WorkflowDispatchOutbox:IndexById:{item.Id}",
        SerializedValue = $"Elsa:WorkflowDispatchOutbox:Index:{item.CreatedAt.UtcTicks:D20}:{item.Id}"
    };

    private static SerializedKeyValuePair CreateRecoveryRecord(WorkflowDispatchOutboxItem item) => new()
    {
        Key = $"Elsa:WorkflowDispatchOutbox:Recovery:{item.Id}",
        SerializedValue = item.Id
    };

    private const string DefaultLegacyScanCompletedKey = "Elsa:WorkflowDispatchOutbox:State:LegacyScanCompleted:";

    private static SerializedKeyValuePair CreateLegacyScanCompletedRecord() => new()
    {
        Key = DefaultLegacyScanCompletedKey,
        SerializedValue = "true"
    };

    private static async Task SaveAsync(IKeyValueStore keyValueStore, SerializedKeyValuePair record)
    {
        await keyValueStore.SaveAsync(record, CancellationToken.None);
    }

    private static async Task<SerializedKeyValuePair?> FindAsync(IKeyValueStore keyValueStore, string key)
    {
        return await keyValueStore.FindAsync(new KeyValueFilter { Key = key }, CancellationToken.None);
    }

    private class TestPayloadSerializer : IPayloadSerializer
    {
        public IDictionary<string, WorkflowDispatchOutboxItem> Items { get; set; } = new Dictionary<string, WorkflowDispatchOutboxItem>();

        public string Serialize(object payload) => ((WorkflowDispatchOutboxItem)payload).Id;

        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();

        public object Deserialize(string serializedData) => Deserialize<WorkflowDispatchOutboxItem>(serializedData);

        public object Deserialize(string serializedData, Type type) => Deserialize(serializedData);

        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();

        public T Deserialize<T>(string serializedData) => Items.TryGetValue(serializedData, out var item) ? (T)(object)item : default!;

        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();

        public JsonSerializerOptions GetOptions() => new();
    }
}
