using Microsoft.Extensions.Time.Testing;
using System.Security.Claims;
using Flamme.Common.Services;
using Flamme.Identity.Constants;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;
using Flamme.Identity.Options;
using Flamme.Identity.Services;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Flamme.Identity.UnitTests.Services;

public class SessionRevokerTests
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromHours(2);
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly MemoryStore<RevokedSession> _store = new();
    private readonly SessionRevoker _revoker;

    public SessionRevokerTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new IdentityTokenOptions { RefreshTokenLifetime = RefreshTokenLifetime });
        _revoker = new(new MemoryRevokedSessionStore(_store), _clock, options);
    }

    [Test]
    public async Task RevokingASessionLeavesOtherSessionsLive()
    {
        await Assert.That(await _revoker.IsRevokedAsync("session-a")).IsFalse();

        await RevokeAsync("session-a", _clock.GetUtcNow());

        await Assert.That(await _revoker.IsRevokedAsync("session-a")).IsTrue();
        await Assert.That(await _revoker.IsRevokedAsync("session-b")).IsFalse();
    }

    [Test]
    public async Task RevokingASessionTwiceIsHarmless()
    {
        await RevokeAsync("session-a", _clock.GetUtcNow());
        await RevokeAsync("session-a", _clock.GetUtcNow());

        await Assert.That(await _revoker.IsRevokedAsync("session-a")).IsTrue();
        await Assert.That(_store.List()).HasSingleItem();
    }

    [Test]
    public async Task RevocationOutlivesARefreshTokenIssuedNow()
    {
        await RevokeAsync("session-a", _clock.GetUtcNow().AddMinutes(1));

        await Assert.That((await Revocation("session-a")).ExpiresAt > _clock.GetUtcNow() + RefreshTokenLifetime).IsTrue();
    }

    [Test]
    public async Task RevocationOutlivesEveryRefreshTokenTheSessionWentThrough()
    {
        // One of them was issued with a longer lifetime than the one configured now.
        var sessionExpiresAt = _clock.GetUtcNow() + RefreshTokenLifetime * 3;

        await RevokeAsync("session-a", sessionExpiresAt);

        await Assert.That((await Revocation("session-a")).ExpiresAt > sessionExpiresAt).IsTrue();
    }

    [Test]
    public async Task RevokingASessionAgainNeverShortensItsRevocation()
    {
        await RevokeAsync("session-a", _clock.GetUtcNow() + RefreshTokenLifetime * 3);
        var expiresAt = (await Revocation("session-a")).ExpiresAt;

        await RevokeAsync("session-a", _clock.GetUtcNow());

        await Assert.That((await Revocation("session-a")).ExpiresAt).IsEqualTo(expiresAt);
    }

    [Test]
    public async Task RevokingASessionAgainExtendsItsRevocation()
    {
        await RevokeAsync("session-a", _clock.GetUtcNow());
        var revokedAt = (await Revocation("session-a")).RevokedAt;
        var sessionExpiresAt = _clock.GetUtcNow() + RefreshTokenLifetime * 3;
        _clock.Advance(TimeSpan.FromMinutes(1));

        await RevokeAsync("session-a", sessionExpiresAt);

        await Assert.That((await Revocation("session-a")).ExpiresAt > sessionExpiresAt).IsTrue();
        await Assert.That((await Revocation("session-a")).RevokedAt).IsEqualTo(revokedAt);
    }

    [Test]
    public async Task RevokingPrunesExpiredRevocationsOnly()
    {
        await RevokeAsync("expired", _clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromDays(1));
        await RevokeAsync("live", _clock.GetUtcNow());
        _clock.Advance(RefreshTokenLifetime);

        await RevokeAsync("latest", _clock.GetUtcNow());

        await Assert.That(await _revoker.IsRevokedAsync("expired")).IsFalse();
        await Assert.That(await _revoker.IsRevokedAsync("live")).IsTrue();
        await Assert.That(await _revoker.IsRevokedAsync("latest")).IsTrue();
    }

    [Test]
    public async Task SessionIsReadFromTheRefreshToken()
    {
        var identity = Identity((CustomClaimTypes.SessionId, "session-a"), (CustomClaimTypes.SessionExpiresAt, "2000000000"), (JwtRegisteredClaimNames.Exp, "1900000000"));

        var expected = new SignInSession("session-a", DateTimeOffset.FromUnixTimeSeconds(2000000000));
        await Assert.That(SessionRevoker.GetSession(identity, "token-a")).IsEqualTo(expected);
        await Assert.That(SessionRevoker.FindSession(identity)).IsEqualTo(expected);
    }

    [Test]
    public async Task SessionExpiresNoEarlierThanTheRefreshTokenItIsReadFrom()
    {
        var identity = Identity((CustomClaimTypes.SessionId, "session-a"), (CustomClaimTypes.SessionExpiresAt, "1900000000"), (JwtRegisteredClaimNames.Exp, "2000000000"));

        await Assert.That(SessionRevoker.GetSession(identity, "token-a").ExpiresAt).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(2000000000));
    }

    [Test]
    public async Task RefreshTokenWithoutSessionGetsOneDerivedFromTheTokenThatExpiresWithIt()
    {
        var identity = Identity((JwtRegisteredClaimNames.Exp, "2000000000"));

        var session = SessionRevoker.GetSession(identity, "token-a");

        await Assert.That(SessionRevoker.GetSession(identity, "token-a")).IsEqualTo(session);
        await Assert.That(SessionRevoker.GetSession(identity, "token-b").Id).IsNotEqualTo(session.Id);
        await Assert.That(session.Id).DoesNotContain("token-a", StringComparison.Ordinal);
        await Assert.That(session.ExpiresAt).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(2000000000));
    }

    [Test]
    public async Task AnIdentityWithoutSessionNamesNone()
    {
        await Assert.That(SessionRevoker.FindSession(Identity((JwtRegisteredClaimNames.Exp, "2000000000")))).IsNull();
    }

    private ValueTask RevokeAsync(string sessionId, DateTimeOffset sessionExpiresAt) => _revoker.RevokeAsync(new(sessionId, sessionExpiresAt));

    private async Task<RevokedSession> Revocation(string sessionId) => await Assert.That(_store.List()).HasSingleItem(x => x.Id == sessionId);

    private static ClaimsIdentity Identity(params (string Type, string Value)[] claims) => new(claims.Select(x => new Claim(x.Type, x.Value)));
}
