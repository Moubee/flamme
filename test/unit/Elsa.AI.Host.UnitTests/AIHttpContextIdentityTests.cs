using System.Security.Claims;
using Elsa;
using Elsa.AI.Host.Endpoints.AI;
using Microsoft.AspNetCore.Http;

namespace Elsa.AI.Host.UnitTests;

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
        await Assert.That(permissions).IsEquivalentTo(["workflows:read"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
