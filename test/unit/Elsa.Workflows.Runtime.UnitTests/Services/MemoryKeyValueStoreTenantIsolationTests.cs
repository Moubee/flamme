using Elsa.Common.Multitenancy;
using Elsa.Common.Services;
using Elsa.KeyValues.Entities;
using Elsa.KeyValues.Models;
using Elsa.KeyValues.Stores;
using Elsa.Testing.Shared.Multitenancy;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

/// <summary>
/// Memory KeyValue must honor ambient tenant the same way EF does via
/// <c>SetTenantIdFilter</c> / <c>ApplyTenantId</c>. Contracts have no TenantAgnostic flag.
/// </summary>
public class MemoryKeyValueStoreTenantIsolationTests
{
    [Test]
    [DisplayName("FindAsync does not return another tenant's row")]
    public async Task FindAsync_WhenOtherTenant_ReturnsNull()
    {
        var store = CreateStore("tenant-a");
        await SeedMixedTenantsAsync(store);

        var found = await store.FindAsync(new KeyValueFilter { Key = "kv-b" }, CancellationToken.None);

        await Assert.That(found).IsNull();
    }

    [Test]
    [DisplayName("FindAsync returns the ambient tenant's row and keeps * visible")]
    public async Task FindAsync_ReturnsOwnAndAgnosticRows()
    {
        var store = CreateStore("tenant-a");
        await SeedMixedTenantsAsync(store);

        var own = await store.FindAsync(new KeyValueFilter { Key = "kv-a" }, CancellationToken.None);
        var agnostic = await store.FindAsync(new KeyValueFilter { Key = "kv-star" }, CancellationToken.None);

        await Assert.That(own).IsNotNull();
        await Assert.That(own.SerializedValue).IsEqualTo("a");
        await Assert.That(agnostic).IsNotNull();
        await Assert.That(agnostic.SerializedValue).IsEqualTo("star");
    }

    [Test]
    [DisplayName("FindManyAsync hides other tenants and keeps * visible")]
    public async Task FindManyAsync_HidesOtherTenants()
    {
        var store = CreateStore("tenant-a");
        await SeedMixedTenantsAsync(store);

        var found = (await store.FindManyAsync(new KeyValueFilter { StartsWith = true, Key = "kv-" }, CancellationToken.None)).ToList();

        await Assert.That(found.Count).IsEqualTo(2);
        await Assert.That(found).Contains(x => x.Key == "kv-a");
        await Assert.That(found).Contains(x => x.Key == "kv-star");
        await Assert.That(found).DoesNotContain(x => x.Key == "kv-b");
    }

    [Test]
    [DisplayName("FindManyAsync applies Take after tenant admission")]
    public async Task FindManyAsync_AppliesTakeAfterTenantFilter()
    {
        var store = CreateStore("tenant-a");
        await SeedMixedTenantsAsync(store);
        await store.SaveAsync(Pair("kv-a2", "a2", "tenant-a"), CancellationToken.None);

        var found = (await store.FindManyAsync(new KeyValueFilter
        {
            StartsWith = true,
            Key = "kv-",
            OrderByKey = true,
            Take = 3
        }, CancellationToken.None)).ToList();

        await Assert.That(found.Select(x => x.Key).ToList()).IsEquivalentTo(["kv-a", "kv-a2", "kv-star"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(found).DoesNotContain(x => x.Key == "kv-b");
    }

    [Test]
    [DisplayName("DeleteAsync does not remove another tenant's key")]
    public async Task DeleteAsync_DoesNotDeleteOtherTenantKey()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        var tenantA = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-a"));
        var tenantB = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-b"));
        await SeedMixedTenantsAsync(tenantA);

        await tenantA.DeleteAsync("kv-b", CancellationToken.None);
        var remaining = await tenantB.FindAsync(new KeyValueFilter { Key = "kv-b" }, CancellationToken.None);

        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining.SerializedValue).IsEqualTo("b");
    }

    [Test]
    [DisplayName("DeleteAsync removes the ambient tenant's own key")]
    public async Task DeleteAsync_RemovesOwnKey()
    {
        var store = CreateStore("tenant-a");
        await SeedMixedTenantsAsync(store);

        await store.DeleteAsync("kv-a", CancellationToken.None);
        var remaining = await store.FindAsync(new KeyValueFilter { Key = "kv-a" }, CancellationToken.None);

        await Assert.That(remaining).IsNull();
    }

    [Test]
    [DisplayName("DeleteAsync leaves a same-key row after another tenant replaced it")]
    public async Task DeleteAsync_WhenSameKeyWasReplacedByOtherTenant_LeavesReplacement()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        var tenantA = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-a"));
        var tenantB = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-b"));
        backing.Save(Pair("shared", "a", "tenant-a"), x => x.Id);
        backing.Save(Pair("shared", "b", "tenant-b"), x => x.Id);

        await tenantA.DeleteAsync("shared", CancellationToken.None);
        var remaining = await tenantB.FindAsync(new KeyValueFilter { Key = "shared" }, CancellationToken.None);

        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining.TenantId).IsEqualTo("tenant-b");
        await Assert.That(remaining.SerializedValue).IsEqualTo("b");
    }

    [Test]
    [DisplayName("SaveAsync stamps the ambient tenant when TenantId is unset")]
    public async Task SaveAsync_WhenTenantIdUnset_StampsAmbientTenant()
    {
        var store = CreateStore("tenant-a");
        var pair = Pair("kv-new", "new");

        await store.SaveAsync(pair, CancellationToken.None);
        var found = await store.FindAsync(new KeyValueFilter { Key = "kv-new" }, CancellationToken.None);

        await Assert.That(pair.TenantId).IsEqualTo("tenant-a");
        await Assert.That(found).IsNotNull();
        await Assert.That(found.TenantId).IsEqualTo("tenant-a");
    }

    [Test]
    [DisplayName("SaveAsync does not overwrite * or an explicit TenantId")]
    public async Task SaveAsync_DoesNotOverwriteAgnosticOrExplicitTenantId()
    {
        var store = CreateStore("tenant-a");
        var agnostic = Pair("kv-star", "star", Tenant.AgnosticTenantId);
        var explicitTenant = Pair("kv-a", "a", "tenant-a");

        await store.SaveAsync(agnostic, CancellationToken.None);
        await store.SaveAsync(explicitTenant, CancellationToken.None);

        await Assert.That(agnostic.TenantId).IsEqualTo(Tenant.AgnosticTenantId);
        await Assert.That(explicitTenant.TenantId).IsEqualTo("tenant-a");
    }

    [Test]
    [DisplayName("FindManyAsync on the default tenant includes null TenantId rows")]
    public async Task FindManyAsync_WhenAmbientIsDefault_IncludesNullTenantId()
    {
        var store = StoreWithPreassignedRows(Tenant.DefaultTenantId);

        var found = (await store.FindManyAsync(new KeyValueFilter { StartsWith = true, Key = "kv-" }, CancellationToken.None)).ToList();

        await Assert.That(found).HasSingleItem();
        await Assert.That(found[0].Key).IsEqualTo("kv-null");
    }

    [Test]
    [DisplayName("FindManyAsync on a named tenant hides null TenantId rows")]
    public async Task FindManyAsync_WhenAmbientIsNamed_HidesNullTenantId()
    {
        var store = StoreWithPreassignedRows("tenant-a");

        var found = (await store.FindManyAsync(new KeyValueFilter { StartsWith = true, Key = "kv-" }, CancellationToken.None)).ToList();

        await Assert.That(found).HasSingleItem();
        await Assert.That(found[0].Key).IsEqualTo("kv-a");
    }

    [Test]
    [DisplayName("SaveAsync throws when another tenant already owns the key and leaves that value intact")]
    public async Task SaveAsync_WhenOtherTenantOwnsKey_ThrowsAndLeavesOriginal()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        var tenantA = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-a"));
        var tenantB = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-b"));
        await tenantA.SaveAsync(Pair("shared", "a", "tenant-a"), CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            tenantB.SaveAsync(Pair("shared", "b", "tenant-b"), CancellationToken.None));

        await Assert.That(ex.Message).Contains("shared");
        var remaining = await tenantA.FindAsync(new KeyValueFilter { Key = "shared" }, CancellationToken.None);
        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining.SerializedValue).IsEqualTo("a");
        await Assert.That(remaining.TenantId).IsEqualTo("tenant-a");
    }

    [Test]
    [DisplayName("SaveAsync lets only an agnostic writer replace a * key")]
    public async Task SaveAsync_WhenAgnosticKey_NamedTenantThrowsAndAgnosticWriterReplaces()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        var named = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-a"));
        var agnostic = new MemoryKeyValueStore(backing, new TestTenantAccessor(Tenant.AgnosticTenantId));
        await agnostic.SaveAsync(Pair("kv-star", "star", Tenant.AgnosticTenantId), CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            named.SaveAsync(Pair("kv-star", "stolen"), CancellationToken.None));

        var stillStar = await named.FindAsync(new KeyValueFilter { Key = "kv-star" }, CancellationToken.None);
        await Assert.That(stillStar).IsNotNull();
        await Assert.That(stillStar.SerializedValue).IsEqualTo("star");
        await Assert.That(stillStar.TenantId).IsEqualTo(Tenant.AgnosticTenantId);

        await agnostic.SaveAsync(Pair("kv-star", "star-2", Tenant.AgnosticTenantId), CancellationToken.None);
        var replaced = await named.FindAsync(new KeyValueFilter { Key = "kv-star" }, CancellationToken.None);
        await Assert.That(replaced).IsNotNull();
        await Assert.That(replaced.SerializedValue).IsEqualTo("star-2");
        await Assert.That(replaced.TenantId).IsEqualTo(Tenant.AgnosticTenantId);
    }

    [Test]
    [DisplayName("SaveAsync same-tenant update keeps the existing TenantId")]
    public async Task SaveAsync_WhenSameTenant_KeepsExistingTenantId()
    {
        var store = CreateStore("tenant-a");
        await store.SaveAsync(Pair("kv-a", "a", "tenant-a"), CancellationToken.None);

        await store.SaveAsync(Pair("kv-a", "a2"), CancellationToken.None);
        var found = await store.FindAsync(new KeyValueFilter { Key = "kv-a" }, CancellationToken.None);

        await Assert.That(found).IsNotNull();
        await Assert.That(found.SerializedValue).IsEqualTo("a2");
        await Assert.That(found.TenantId).IsEqualTo("tenant-a");
    }

    [Test]
    [DisplayName("SaveAsync by the default tenant keeps a null TenantId")]
    public async Task SaveAsync_WhenDefaultTenantUpdatesNullTenantId_KeepsNull()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        backing.Save(Pair("kv-null", "null"), x => x.Id);
        var store = new MemoryKeyValueStore(backing, new TestTenantAccessor(Tenant.DefaultTenantId));

        await store.SaveAsync(Pair("kv-null", "updated"), CancellationToken.None);
        var found = await store.FindAsync(new KeyValueFilter { Key = "kv-null" }, CancellationToken.None);

        await Assert.That(found).IsNotNull();
        await Assert.That(found.SerializedValue).IsEqualTo("updated");
        await Assert.That(found.TenantId).IsNull();
    }

    [Test]
    [DisplayName("DeleteAsync removes a * key that is visible to the ambient tenant")]
    public async Task DeleteAsync_WhenAgnosticKey_NamedTenantRemovesIt()
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        var tenantB = new MemoryKeyValueStore(backing, new TestTenantAccessor("tenant-b"));
        backing.Save(Pair("kv-star", "star", Tenant.AgnosticTenantId), x => x.Id);

        await tenantB.DeleteAsync("kv-star", CancellationToken.None);
        var remaining = await tenantB.FindAsync(new KeyValueFilter { Key = "kv-star" }, CancellationToken.None);

        await Assert.That(remaining).IsNull();
    }

    private static MemoryKeyValueStore CreateStore(string tenantId) =>
        new(new MemoryStore<SerializedKeyValuePair>(), new TestTenantAccessor(tenantId));

    private static MemoryKeyValueStore StoreWithPreassignedRows(string ambientTenantId)
    {
        var backing = new MemoryStore<SerializedKeyValuePair>();
        backing.Save(Pair("kv-null", "null"), x => x.Id);
        backing.Save(Pair("kv-a", "a", "tenant-a"), x => x.Id);
        return new MemoryKeyValueStore(backing, new TestTenantAccessor(ambientTenantId));
    }

    private static async Task SeedMixedTenantsAsync(MemoryKeyValueStore store)
    {
        await store.SaveAsync(Pair("kv-a", "a", "tenant-a"), CancellationToken.None);
        await store.SaveAsync(Pair("kv-b", "b", "tenant-b"), CancellationToken.None);
        await store.SaveAsync(Pair("kv-star", "star", Tenant.AgnosticTenantId), CancellationToken.None);
    }

    private static SerializedKeyValuePair Pair(string key, string value, string? tenantId = null) =>
        new()
        {
            Key = key,
            SerializedValue = value,
            TenantId = tenantId
        };
}
