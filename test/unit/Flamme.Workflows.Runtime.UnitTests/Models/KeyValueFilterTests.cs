using Flamme.KeyValues.Entities;
using Flamme.KeyValues.Models;

namespace Flamme.Workflows.Runtime.UnitTests.Models;

public class KeyValueFilterTests
{
    [Test]
    public async Task Apply_OrdersByPersistedKey_WhenOrderByKeyIsEnabled()
    {
        var filter = new KeyValueFilter { OrderByKey = true };

        var result = filter.Apply(CreateUnorderedPairs()).Select(x => x.Key).ToList();

        await Assert.That(result).IsEquivalentTo(["key-a", "key-b", "key-c"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Apply_AppliesTakeAfterOrderingByPersistedKey()
    {
        var filter = new KeyValueFilter { OrderByKey = true, Take = 2 };

        var result = filter.Apply(CreateUnorderedPairs()).Select(x => x.Key).ToList();

        await Assert.That(result).IsEquivalentTo(["key-a", "key-b"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Apply_ReturnsNoRecords_WhenTakeIsZero()
    {
        var filter = new KeyValueFilter { Take = 0 };

        var result = filter.Apply(CreateUnorderedPairs()).ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Apply_DoesNotLimitRecords_WhenTakeIsNull()
    {
        var filter = new KeyValueFilter { Take = null };

        var result = filter.Apply(CreateUnorderedPairs()).Select(x => x.Key).ToList();

        await Assert.That(result).IsEquivalentTo(["key-c", "key-a", "key-b"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This JIT-only fixture uses LINQ-to-Objects to exercise the production IQueryable filtering contract.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-only fixture uses LINQ-to-Objects to exercise the production IQueryable filtering contract.")]
    private static IQueryable<SerializedKeyValuePair> CreateUnorderedPairs()
    {
        return new[]
        {
            new SerializedKeyValuePair { Key = "key-c", SerializedValue = "c" },
            new SerializedKeyValuePair { Key = "key-a", SerializedValue = "a" },
            new SerializedKeyValuePair { Key = "key-b", SerializedValue = "b" }
        }.AsQueryable();
    }
}
