namespace Elsa.Persistence.EFCore.UnitTests;

[InheritsTests]
public sealed class SqliteStoreSaveManyTenantOwnershipTests : StoreSaveManyTenantOwnershipTests
{
    protected override Task<OwnershipStoreScenario> CreateScenarioAsync(string tenantId, bool tenantsEnabled = true) =>
        OwnershipStoreScenario.CreateSqliteAsync(tenantId, tenantsEnabled);

    [Test]
    public async Task SaveManyAsync_WhenNocaseKeyDiffersOnlyInCasing_ThrowsAndLeavesOwner()
    {
        await using var scenario = await OwnershipStoreScenario.CreateSqliteAsync("tenant-a", tenantsEnabled: true, keyCollation: "NOCASE");
        await scenario.Store.SaveManyAsync([new OwnedRow { Id = "abc", TenantId = "tenant-a", Payload = "original" }], x => x.Id, onSaving: null);

        using (scenario.UseTenant("tenant-b"))
        {
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                scenario.Store.SaveManyAsync([new OwnedRow { Id = "ABC", TenantId = "tenant-b", Payload = "stolen" }], x => x.Id, onSaving: null));

            // The error names tenant-b's own spelling, never tenant-a's stored key.
            await Assert.That(exception.Message).Contains("'ABC'");
            await Assert.That(exception.Message).DoesNotContain("'abc'");
        }

        await AssertOriginalOwnerAsync(scenario);
    }

    [Test]
    public async Task SaveManyAsync_WhenNocaseBatchPairsExactKeyWithForgedVariant_ThrowsAndLeavesOwner()
    {
        await using var scenario = await OwnershipStoreScenario.CreateSqliteAsync("tenant-a", tenantsEnabled: true, keyCollation: "NOCASE");
        await scenario.Store.SaveManyAsync([new OwnedRow { Id = "abc", TenantId = "tenant-a", Payload = "original" }], x => x.Id, onSaving: null);

        // The exact key passes its own check; the variant targets the same row under NOCASE with a forged tenant.
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.Store.SaveManyAsync(
            [
                new OwnedRow { Id = "abc", TenantId = "tenant-a", Payload = "original" },
                new OwnedRow { Id = "ABC", TenantId = "tenant-b", Payload = "stolen" }
            ],
            x => x.Id,
            onSaving: null));

        await Assert.That(exception.Message).Contains("'ABC'");
        await AssertOriginalOwnerAsync(scenario);
    }

    [Test]
    public async Task SaveManyAsync_WhenTrailingSpaceVariantTargetsOwnedRow_ThrowsAndLeavesOwner()
    {
        // SQLite RTRIM stands in for SQL Server collations that ignore trailing spaces.
        await using var scenario = await OwnershipStoreScenario.CreateSqliteAsync("tenant-a", tenantsEnabled: true, keyCollation: "RTRIM");
        await scenario.Store.SaveManyAsync([new OwnedRow { Id = "abc", TenantId = "tenant-a", Payload = "original" }], x => x.Id, onSaving: null);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.Store.SaveManyAsync(
            [
                new OwnedRow { Id = "abc", TenantId = "tenant-a", Payload = "original" },
                new OwnedRow { Id = "ABC", TenantId = "tenant-a", Payload = "distinct under RTRIM" },
                new OwnedRow { Id = "abc ", TenantId = "tenant-b", Payload = "stolen" }
            ],
            x => x.Id,
            onSaving: null));

        // Only the colliding spelling is named; ABC is a distinct key under RTRIM.
        await Assert.That(exception.Message).Contains("'abc '");
        await Assert.That(exception.Message).DoesNotContain("'ABC'");
        await AssertOriginalOwnerAsync(scenario);
    }

    [Test]
    public async Task SaveManyAsync_WhenCollidingVariantCannotBeNamed_DoesNotRevealStoredKey()
    {
        // Under NOCASE trailing spaces stay significant: eleven "SECRET-A   " spellings look like variants
        // but reach no row, exhausting the naming probes before the colliding "SECRET-A". The generic
        // error must still not reveal the stored key.
        await using var scenario = await OwnershipStoreScenario.CreateSqliteAsync("tenant-a", tenantsEnabled: true, keyCollation: "NOCASE");
        await scenario.Store.SaveManyAsync([new OwnedRow { Id = "secret-a", TenantId = "tenant-a", Payload = "original" }], x => x.Id, onSaving: null);

        using (scenario.UseTenant("tenant-b"))
        {
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.Store.SaveManyAsync(
                Enumerable.Range(0, 11).Select(i => new OwnedRow { Id = "SECRET-A" + new string(' ', i + 1), TenantId = "tenant-b", Payload = "noise" })
                    .Append(new OwnedRow { Id = "SECRET-A", TenantId = "tenant-b", Payload = "stolen" })
                    .ToList(),
                x => x.Id,
                onSaving: null));

            await Assert.That(exception.Message).Contains("matches an existing row");
            await Assert.That(exception.Message).DoesNotContain("secret-a");
        }

        var remaining = await scenario.FindAsync("secret-a");
        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining.TenantId).IsEqualTo("tenant-a");
        await Assert.That(remaining.Payload).IsEqualTo("original");
    }

    [Test]
    public async Task SaveManyAsync_WhenAccentVariantTargetsOtherTenantsRow_NamesSubmittedKeyOnly()
    {
        await using var scenario = await OwnershipStoreScenario.CreateSqliteAsync("tenant-a", tenantsEnabled: true, keyCollation: OwnershipStoreScenario.NoAccentCollation);
        await scenario.Store.SaveManyAsync([new OwnedRow { Id = "café", TenantId = "tenant-a", Payload = "original" }], x => x.Id, onSaving: null);

        using (scenario.UseTenant("tenant-b"))
        {
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.Store.SaveManyAsync(
                [
                    new OwnedRow { Id = "new-from-b", TenantId = "tenant-b", Payload = "should-not-land" },
                    new OwnedRow { Id = "cafe", TenantId = "tenant-b", Payload = "stolen" }
                ],
                x => x.Id,
                onSaving: null));

            await Assert.That(exception.Message).Contains("'cafe'");
            await Assert.That(exception.Message).DoesNotContain("café");
        }

        await Assert.That(await scenario.FindAsync("new-from-b")).IsNull();
        var remaining = await scenario.FindAsync("café");
        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining.Id).IsEqualTo("café");
        await Assert.That(remaining.TenantId).IsEqualTo("tenant-a");
        await Assert.That(remaining.Payload).IsEqualTo("original");
    }

    private static async Task AssertOriginalOwnerAsync(OwnershipStoreScenario scenario)
    {
        var remaining = await scenario.FindAsync("abc");
        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining.Id).IsEqualTo("abc");
        await Assert.That(remaining.TenantId).IsEqualTo("tenant-a");
        await Assert.That(remaining.Payload).IsEqualTo("original");
    }
}
