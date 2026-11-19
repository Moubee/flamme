using System.Security.Claims;
using System.Text.Json;
using Flamme.Authorization;
using Flamme.ExternalAuthentication.Models;
using Flamme.ExternalAuthentication.Options;
using Flamme.ExternalAuthentication.Permissions;
using Flamme.ExternalAuthentication.Services;
using Flamme.Identity.Models;

namespace Flamme.ExternalAuthentication.IntegrationTests.Permissions;

public class PermissionDelegationTests
{
    [Test]
    public async Task OrdinaryAdministratorMustPossessDelegationPermissionAndEveryGrantedPermission()
    {
        var authorizer = new DefaultPermissionDelegationAuthorizer(Microsoft.Extensions.Options.Options.Create(new ExternalAuthenticationOptions()), PermissionEvaluator.Shared);
        var selection = Selection("reports:view");

        var withoutGrantedPermission = await authorizer.AuthorizeAsync(
            Actor(DelegatePermission),
            [selection]);
        var withGrantedPermission = await authorizer.AuthorizeAsync(
            Actor(DelegatePermission, "reports:view"),
            [selection]);

        await Assert.That(withoutGrantedPermission.IsAuthorized).IsFalse();
        await Assert.That(withoutGrantedPermission.UnauthorizedPermissions).IsEquivalentTo(
            ["reports:view"],
            StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(withGrantedPermission.IsAuthorized).IsTrue();
    }

    [Test]
    public async Task UnrestrictedDelegationCannotCrossDeploymentDenyBoundary()
    {
        var options = new ExternalAuthenticationOptions();
        options.PermissionGrants.DeniedPermissions = ["reports:view"];
        var authorizer = new DefaultPermissionDelegationAuthorizer(Microsoft.Extensions.Options.Options.Create(options), PermissionEvaluator.Shared);

        var result = await authorizer.AuthorizeAsync(
            Actor(DelegateUnrestrictedPermission, PermissionNames.All),
            [Selection("reports:view")]);

        await Assert.That(result.IsAuthorized).IsFalse();
        await Assert.That(result.UnauthorizedPermissions).IsEquivalentTo(
            ["reports:view"],
            StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private const string DelegatePermission = $"{ExternalAuthenticationResourcePermissions.PermissionGrants}:{ExternalAuthenticationVerbs.Delegate}";
    private const string DelegateUnrestrictedPermission = $"{ExternalAuthenticationResourcePermissions.PermissionGrants}:{ExternalAuthenticationVerbs.DelegateUnrestricted}";

    private static GrantSourceSelection Selection(string permission) => new(
        "claim-mapping",
        1,
        TestJson.Element(new JsonObject
        {
            ["claimType"] = "department",
            ["mappings"] = new JsonObject { ["engineering"] = new JsonArray(permission) }
        }),
        0);

    private static ClaimsPrincipal Actor(params string[] permissions) => new(
        new ClaimsIdentity(permissions.Select(permission => new Claim(PermissionNames.ClaimType, permission)), "test"));
}
