using System.Security.Claims;
using Flamme;
using Flamme.AI.Host.Endpoints.AI;
using Microsoft.AspNetCore.Http;

namespace Flamme.AI.Host.UnitTests;

public class AIHttpContextIdentityTests
{
    [Test]
    public async Task GetPermissionsOmitsTheEmptySetSentinel()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PermissionNames.ClaimType, PermissionNames.None),
                new Claim(PermissionNames.ClaimType, "workflows:read")
            ], "test"))
        };

        var permissions = AIHttpContextIdentity.GetPermissions(context);

        await Assert.That(permissions).DoesNotContain(PermissionNames.None);
        await Assert.That(permissions).IsEquivalentTo(["workflows:read"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetPermissionsIsEmptyWhenTheCallerHoldsOnlyTheSentinel()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PermissionNames.ClaimType, PermissionNames.None)
            ], "test"))
        };

        await Assert.That(AIHttpContextIdentity.GetPermissions(context)).IsEmpty();
    }
}
