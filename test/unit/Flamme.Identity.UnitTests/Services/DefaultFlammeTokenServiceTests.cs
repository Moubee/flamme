using Microsoft.Extensions.Time.Testing;
using System.Security.Claims;
using System.Text.Json;
using Flamme;
using Flamme.Authorization;
using Flamme.Common;
using Flamme.Identity.Constants;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;
using Flamme.Identity.Options;
using Flamme.Identity.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flamme.Identity.UnitTests.Services;

public class DefaultFlammeTokenServiceTests
{
    [Test]
    [DisplayName("Token issuance context is projected into a Flamme access token")]
    public async Task IssueAccessTokenProjectsContext()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = Microsoft.Extensions.Options.Options.Create(new IdentityTokenOptions
        {
            SigningKey = "external-authentication-test-signing-key",
            Issuer = "https://elsa.test",
            Audience = "elsa-api",
            AccessTokenLifetime = TimeSpan.FromMinutes(15)
        });
        var service = new DefaultFlammeTokenService(clock, options);
        var user = new User { Id = "user-1", Name = "alice", TenantId = "tenant-a" };
        var context = new TokenIssuanceContext(
            user,
            ["operator"],
            ["workflows:read"],
            [new Claim("department", "claims")],
            "session-1");

        var result = await service.IssueAccessTokenAsync(context);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(result.Token);

        await Assert.That(result.ExpiresAt).IsEqualTo(clock.GetUtcNow().AddMinutes(15));
        await Assert.That(token.Claims).Contains(x => x.Type == JwtRegisteredClaimNames.Sub && x.Value == user.Id);
        await Assert.That(token.Claims).Contains(x => x.Type == JwtRegisteredClaimNames.Name && x.Value == user.Name);
        await Assert.That(token.Claims).Contains(x => x.Type == options.Value.TenantIdClaimsType && x.Value == user.TenantId);
        await Assert.That(token.Claims).Contains(x => x.Type == ClaimTypes.Role && x.Value == "operator");
        await Assert.That(token.Claims).Contains(x => x.Type == "permissions" && x.Value == "workflows:read");
        await Assert.That(token.Claims).Contains(x => x.Type == "department" && x.Value == "claims");
        await Assert.That(token.Claims).Contains(x => x.Type == CustomClaimTypes.ExternalAuthenticationSessionId && x.Value == "session-1");
        await Assert.That(token.Claims).Contains(x => x.Type == TokenUse.ClaimType && x.Value == TokenUse.Access);
    }

    [Test]
    [DisplayName("A refresh token always carries the user's subject")]
    public async Task IssueRefreshTokenAlwaysEmitsSubject()
    {
        var user = new User { Id = "user-1", Name = "alice" };
        var token = await CreateService().IssueRefreshTokenAsync(new TokenIssuanceContext(user, [], [], []));
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);

        await Assert.That(jwt.Claims).Contains(x => x.Type == JwtRegisteredClaimNames.Sub && x.Value == user.Id);
        await Assert.That(jwt.Claims).Contains(x => x.Type == JwtRegisteredClaimNames.Name && x.Value == user.Name);
    }

    [Test]
    [DisplayName("A zero-grant token carries the permissions sentinel, not an omitted claim")]
    public async Task ZeroGrantsEmitTheEmptySetSentinel()
    {
        var result = await CreateService().IssueAccessTokenAsync(EmptyGrantContext());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(result.Token);
        var payload = StudioWasmJwtParser.ReadPayload(result.Token);

        await Assert.That(jwt.Claims).Contains(x => x.Type == PermissionNames.ClaimType && x.Value == PermissionNames.None);
        await Assert.That(jwt.Claims).HasSingleItem(x => x.Type == PermissionNames.ClaimType);
        // An empty-string claim survives as a single claim both server-side and in Studio's JwtParser.
        // Only an empty JSON array (`[]`) disappears. A non-empty string that is not a permission
        // also survives both and is not a grant.
        await Assert.That(payload.GetProperty(PermissionNames.ClaimType).ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(payload.GetProperty(PermissionNames.ClaimType).GetString()).IsEqualTo(PermissionNames.None);
    }

    [Test]
    [DisplayName("A refresh token with zero grants carries the same sentinel as the access token")]
    public async Task ZeroGrantRefreshTokensCarryTheSentinel()
    {
        var result = await CreateService().IssueRefreshTokenAsync(EmptyGrantContext());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(result.Token);

        await Assert.That(jwt.Claims).Contains(x => x.Type == PermissionNames.ClaimType && x.Value == PermissionNames.None);
    }

    [Test]
    [DisplayName("The empty-set sentinel survives JwtBearer validation and Studio WASM parsing as a known empty set")]
    public async Task EmptySetSentinelRoundTripsToAKnownEmptySet()
    {
        var options = CreateOptions();
        var result = await new DefaultFlammeTokenService(new FakeTimeProvider(DateTimeOffset.UtcNow), options).IssueAccessTokenAsync(EmptyGrantContext());

        var jwtBearerClaims = await ValidateWithJwtBearerAsync(result.Token, options.Value);
        var wasmClaims = StudioWasmJwtParser.Parse(result.Token);

        await AssertKnownEmpty(jwtBearerClaims);
        await AssertKnownEmpty(wasmClaims);
        await Assert.That(new PermissionEvaluator().GetGrants(new ClaimsPrincipal(new ClaimsIdentity(jwtBearerClaims, "jwt")))).IsEmpty();
    }

    [Test]
    [DisplayName("A token that omits the permissions claim stays without grants after JwtBearer and WASM parsing")]
    public async Task AMissingPermissionsClaimStaysUnknown()
    {
        var options = CreateOptions().Value;
        var token = CreateTokenWithoutPermissionsClaim(options);

        var jwtBearerClaims = await ValidateWithJwtBearerAsync(token, options);
        var wasmClaims = StudioWasmJwtParser.Parse(token);

        await Assert.That(jwtBearerClaims).DoesNotContain(x => x.Type == PermissionNames.ClaimType);
        await Assert.That(wasmClaims).DoesNotContain(x => x.Type == PermissionNames.ClaimType);
        // Studio's ClaimsPermissionService maps "no permissions claims" to UserPermissions.Unknown.
        await Assert.That(HasPermissionsClaim(jwtBearerClaims)).IsFalse();
        await Assert.That(HasPermissionsClaim(wasmClaims)).IsFalse();
    }

    [Test]
    [DisplayName("An empty JSON array permissions claim is unknown after Studio WASM parsing")]
    public async Task EmptyJsonArrayIsUnknownAfterStudioWasmParsing()
    {
        var options = CreateOptions().Value;
        var token = CreateTokenWithPermissionsPayload(options, Array.Empty<string>());
        var payload = StudioWasmJwtParser.ReadPayload(token);

        // Only an empty JSON array (`[]`) disappears: Studio's WASM parser expands arrays item-by-item
        // and therefore produces zero permissions claims — UserPermissions.Unknown. An empty-string
        // claim survives as a single claim both server-side and in Studio's JwtParser.
        await Assert.That(payload.GetProperty(PermissionNames.ClaimType).ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(payload.GetProperty(PermissionNames.ClaimType).GetArrayLength()).IsEqualTo(0);
        await Assert.That(HasPermissionsClaim(StudioWasmJwtParser.Parse(token))).IsFalse();
    }

    [Test]
    [DisplayName("A refresh token starts a new session unless the context continues one")]
    public async Task IssueRefreshTokenCarriesTheSession()
    {
        var service = CreateService();
        var context = new TokenIssuanceContext(new User { Id = "user-1", Name = "alice" }, [], [], []);

        var first = ReadSessionId(await service.IssueRefreshTokenAsync(context));
        var second = ReadSessionId(await service.IssueRefreshTokenAsync(context));
        var continued = ReadSessionId(await service.IssueRefreshTokenAsync(context with { Session = new(first!, DateTimeOffset.UtcNow) }));

        await Assert.That(string.IsNullOrWhiteSpace(first)).IsFalse();
        await Assert.That(second).IsNotEqualTo(first);
        await Assert.That(continued).IsEqualTo(first);
    }

    [Test]
    [DisplayName("An access token carries no session, because revoking one does not revoke access tokens")]
    public async Task IssueAccessTokenCarriesNoSession()
    {
        var context = new TokenIssuanceContext(new User { Id = "user-1", Name = "alice" }, [], [], []) { Session = new("session-1", DateTimeOffset.UtcNow) };

        var token = await CreateService().IssueAccessTokenAsync(context);

        await Assert.That(new JsonWebTokenHandler().ReadJsonWebToken(token.Token).Claims).DoesNotContain(x => x.Type == CustomClaimTypes.SessionId);
    }

    [Test]
    [DisplayName("A refresh token carries the latest expiry of its session forward, and its own when that is later")]
    public async Task IssueRefreshTokenCarriesTheSessionExpiryForward()
    {
        var service = CreateService();
        var context = new TokenIssuanceContext(new User { Id = "user-1", Name = "alice" }, [], [], []);
        var longLived = new SignInSession("session-1", DateTimeOffset.UtcNow.AddDays(30));
        var shortLived = longLived with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };

        var started = await service.IssueRefreshTokenAsync(context);
        var continuedLongLived = await service.IssueRefreshTokenAsync(context with { Session = longLived });
        var continuedShortLived = await service.IssueRefreshTokenAsync(context with { Session = shortLived });

        await Assert.That(ReadSessionExpiresAt(started)).IsEqualTo(started.ExpiresAt.ToUnixTimeSeconds());
        await Assert.That(ReadSessionExpiresAt(continuedLongLived)).IsEqualTo(longLived.ExpiresAt.ToUnixTimeSeconds());
        await Assert.That(ReadSessionExpiresAt(continuedShortLived)).IsEqualTo(continuedShortLived.ExpiresAt.ToUnixTimeSeconds());
    }

    private static DefaultFlammeTokenService CreateService() =>
        new(new FakeTimeProvider(DateTimeOffset.UtcNow), CreateOptions());

    private static IOptions<IdentityTokenOptions> CreateOptions() =>
        Microsoft.Extensions.Options.Options.Create(new IdentityTokenOptions { SigningKey = IdentityTokenTestConstants.SigningKey });

    private static TokenIssuanceContext EmptyGrantContext() =>
        new(new User { Id = "user-1", Name = "alice" }, [], [], []);

    private static string? ReadSessionId(IssuedAccessToken token) =>
        new JsonWebTokenHandler().ReadJsonWebToken(token.Token).Claims.FirstOrDefault(x => x.Type == CustomClaimTypes.SessionId)?.Value;

    private static long ReadSessionExpiresAt(IssuedAccessToken token) =>
        long.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token.Token).GetClaim(CustomClaimTypes.SessionExpiresAt).Value);

    private static async Task<IReadOnlyCollection<Claim>> ValidateWithJwtBearerAsync(string token, IdentityTokenOptions options)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, options.CreateTokenValidationParameters());
        await Assert.That(result.IsValid).IsTrue().Because(result.Exception?.Message ?? "Token validation must succeed.");
        return result.ClaimsIdentity.Claims.ToArray();
    }

    private static bool HasPermissionsClaim(IEnumerable<Claim> claims) =>
        claims.Any(x => x.Type == PermissionNames.ClaimType);
    private static async Task AssertKnownEmpty(IEnumerable<Claim> claims)
    {
        var permissionClaims = claims.Where(x => x.Type == PermissionNames.ClaimType).ToList();
        await Assert.That(permissionClaims).IsNotEmpty();
        foreach (var claim in permissionClaims)
        {
            await Assert.That(Permission.TryParse(claim.Value, out _)).IsFalse();
        }
    }

    private static string CreateTokenWithoutPermissionsClaim(IdentityTokenOptions options) =>
        CreateTokenWithPermissionsPayload(options, permissions: null);

    private static string CreateTokenWithPermissionsPayload(IdentityTokenOptions options, object? permissions)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = "user-1",
            [JwtRegisteredClaimNames.Name] = "alice",
            [TokenUse.ClaimType] = TokenUse.Access
        };

        if (permissions is not null)
        {
            claims[PermissionNames.ClaimType] = permissions;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Issuer = options.Issuer,
            Audience = options.Audience,
            SigningCredentials = new SigningCredentials(options.CreateSecurityKey(), SecurityAlgorithms.HmacSha256Signature)
        });
    }

}