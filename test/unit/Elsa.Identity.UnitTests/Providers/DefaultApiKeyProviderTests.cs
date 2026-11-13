using AspNetCore.Authentication.ApiKey;
using Elsa;
using Elsa.Identity.Contracts;
using Elsa.Identity.Entities;
using Elsa.Identity.Models;
using Elsa.Identity.Providers;
using NSubstitute;

namespace Elsa.Identity.UnitTests.Providers;

public class DefaultApiKeyProviderTests
{
    [Test]
    public async Task AZeroGrantApplicationGetsTheEmptySetSentinel()
    {
        var apiKey = await ProvideAsync("noperm", permissions: []);

        await Assert.That(apiKey).IsNotNull();
        await Assert.That(apiKey.OwnerName).IsEqualTo("client-noperm");
        await Assert.That((await Assert.That(apiKey.Claims).HasSingleItem(x => x.Type == PermissionNames.ClaimType)).Value).IsEqualTo(PermissionNames.None);
    }

    [Test]
    public async Task AGrantedApplicationKeepsItsPermissions()
    {
        var apiKey = await ProvideAsync("reader", permissions: ["identity/users:view", "identity/roles:view"]);

        await Assert.That(apiKey).IsNotNull();
        await Assert.That(apiKey.Claims.Where(x => x.Type == PermissionNames.ClaimType).Select(x => x.Value).ToArray()).IsEquivalentTo(new[] { "identity/users:view", "identity/roles:view" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(apiKey.Claims).DoesNotContain(x => x.Type == PermissionNames.ClaimType && x.Value == PermissionNames.None);
    }

    private static async Task<IApiKey?> ProvideAsync(string roleId, IReadOnlyCollection<string> permissions)
    {
        var application = new Application { ClientId = $"client-{roleId}", Roles = [roleId] };
        var validator = Substitute.For<IApplicationCredentialsValidator>();
        validator.ValidateAsync("key", Arg.Any<CancellationToken>()).Returns(application);

        var roles = Substitute.For<IRoleProvider>();
        roles.FindManyAsync(Arg.Any<RoleFilter>(), Arg.Any<CancellationToken>())
            .Returns([new Role { Id = roleId, Name = roleId, Permissions = permissions.ToList() }]);

        return await new DefaultApiKeyProvider(validator, roles).ProvideAsync("key");
    }
}
