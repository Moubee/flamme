using Flamme.Authorization;

namespace Flamme.Api.Common.UnitTests.Authorization;

/// <summary>
/// The registry reports every declaration through its requirement accessors, while the single-permission accessors that
/// predate "any of" requirements keep reporting exactly what they did: the one permission an endpoint requires.
/// </summary>
public class EndpointPermissionRegistryTests
{
    private static readonly Permission Alpha = new("tests/alpha", CoreVerbs.View);
    private static readonly Permission Beta = new("tests/beta", CoreVerbs.View);

    [Test]
    public async Task ASinglePermission_IsReportedByEveryAccessor()
    {
        EndpointPermissionRegistry.Record(typeof(SinglePermissionEndpoint), Alpha);

        await Assert.That(EndpointPermissionRegistry.Find(typeof(SinglePermissionEndpoint))).IsEqualTo(Alpha);
        await Assert.That(EndpointPermissionRegistry.All[typeof(SinglePermissionEndpoint)]).IsEqualTo(Alpha);
        await Assert.That(EndpointPermissionRegistry.FindRequirement(typeof(SinglePermissionEndpoint))!.AnyOf).IsEquivalentTo([Alpha], EqualityComparer<Permission>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(EndpointPermissionRegistry.AllRequirements[typeof(SinglePermissionEndpoint)].AnyOf).IsEquivalentTo([Alpha], EqualityComparer<Permission>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnAnyOfRequirement_IsReportedOnlyByTheRequirementAccessors()
    {
        EndpointPermissionRegistry.Record(typeof(AnyOfEndpoint), new EndpointPermissionRequirement([Alpha, Beta]));

        await Assert.That(EndpointPermissionRegistry.FindRequirement(typeof(AnyOfEndpoint))!.AnyOf).IsEquivalentTo([Alpha, Beta], EqualityComparer<Permission>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(EndpointPermissionRegistry.AllRequirements[typeof(AnyOfEndpoint)].AnyOf).IsEquivalentTo([Alpha, Beta], EqualityComparer<Permission>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        // A single permission cannot express a choice, so these report nothing rather than one of the two.
        await Assert.That(EndpointPermissionRegistry.Find(typeof(AnyOfEndpoint))).IsNull();
        await Assert.That(EndpointPermissionRegistry.All.ContainsKey(typeof(AnyOfEndpoint))).IsFalse();
    }

    [Test]
    public async Task RecordingAgain_ReplacesTheEarlierDeclarationInEveryAccessor()
    {
        EndpointPermissionRegistry.Record(typeof(RedeclaredEndpoint), Alpha);
        EndpointPermissionRegistry.Record(typeof(RedeclaredEndpoint), new EndpointPermissionRequirement([Alpha, Beta]));

        await Assert.That(EndpointPermissionRegistry.Find(typeof(RedeclaredEndpoint))).IsNull();
        await Assert.That(EndpointPermissionRegistry.All.ContainsKey(typeof(RedeclaredEndpoint))).IsFalse();

        EndpointPermissionRegistry.Record(typeof(RedeclaredEndpoint), Beta);

        await Assert.That(EndpointPermissionRegistry.Find(typeof(RedeclaredEndpoint))).IsEqualTo(Beta);
        await Assert.That(EndpointPermissionRegistry.FindRequirement(typeof(RedeclaredEndpoint))!.AnyOf).IsEquivalentTo([Beta], EqualityComparer<Permission>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ARequirement_CollapsesDuplicatePermissions() =>
        await Assert.That(new EndpointPermissionRequirement([Alpha, Alpha]).AnyOf).IsEquivalentTo([Alpha], EqualityComparer<Permission>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);

    [Test]
    public void ARequirementWithNoPermissions_IsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() => new EndpointPermissionRequirement([]));

    // The registry is process-global, so each test records against a type of its own.
    private sealed class SinglePermissionEndpoint;

    private sealed class AnyOfEndpoint;

    private sealed class RedeclaredEndpoint;
}
