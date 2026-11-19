using Microsoft.Extensions.Time.Testing;
using System.Security.Claims;
using Flamme.Common.Multitenancy;
using Flamme.Common.Services;
using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;
using Flamme.Identity.Options;
using Flamme.Identity.Services;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Threading.Tasks;

namespace Flamme.Identity.UnitTests.Services;

public class DefaultIdentityRefreshTokenServiceTests
{
    private static readonly User User = new() { Id = "user-a", Name = "admin" };
    private static readonly User Victim = new() { Id = "victim-id", Name = "victim" };
    private static readonly IssuedTokens RefreshedTokens = new("access-b", "refresh-b");
    private readonly IdentityTokenOptions _options = new()
    {
        SigningKey = IdentityTokenTestConstants.SigningKey,
        Issuer = "https://elsa.test",
        Audience = "elsa-api"
    };
    private readonly IAccessTokenIssuer _accessTokenIssuer = IAccessTokenIssuer.Mock().Object;
    private readonly IUserProvider _userProvider = IUserProvider.Mock().Object;
    private readonly DefaultFlammeTokenService _tokenService;
    private readonly SessionRevoker _sessionRevoker;
    private readonly DefaultIdentityRefreshTokenService _service;

    public DefaultIdentityRefreshTokenServiceTests()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = Microsoft.Extensions.Options.Options.Create(_options);
        Mock.Get(_userProvider).FindAsync(Arg.Is<UserFilter>(x => x.Id == User.Id), Arg.Any<CancellationToken>()).Returns(User);
        Mock.Get(_userProvider).FindAsync(Arg.Is<UserFilter>(x => x.Id == Victim.Id), Arg.Any<CancellationToken>()).Returns(Victim);
        Mock.Get(_accessTokenIssuer).IssueTokensAsync(User, Arg.Any<SignInSession?>(), Arg.Any<CancellationToken>()).Returns(RefreshedTokens);
        Mock.Get(_accessTokenIssuer).IssueTokensAsync(Victim, Arg.Any<SignInSession?>(), Arg.Any<CancellationToken>()).Returns(new IssuedTokens("access-victim", "refresh-victim"));
        _tokenService = new(clock, options);
        _sessionRevoker = new(new MemoryRevokedSessionStore(new MemoryStore<RevokedSession>()), clock, options);
        _service = new(_userProvider, _accessTokenIssuer, new DefaultTenantAccessor(), _sessionRevoker, options);
    }

    [Test]
    public async Task RefreshAsyncRejectsAccessAndTamperedTokens()
    {
        var context = new TokenIssuanceContext(User, [], [], []);
        var accessToken = await _tokenService.IssueAccessTokenAsync(context);
        var refreshToken = (await _tokenService.IssueRefreshTokenAsync(context)).Token;

        // The first character of the signature: all of its bits count. Only some of the last one's do, so changing
        // that one can leave the signature intact.
        var signature = refreshToken.LastIndexOf('.') + 1;
        var tamperedRefreshToken = refreshToken[..signature] + (refreshToken[signature] == 'A' ? 'B' : 'A') + refreshToken[(signature + 1)..];

        await Assert.That(await _service.RefreshAsync(accessToken.Token)).IsNull();
        await Assert.That(await _service.RefreshAsync(tamperedRefreshToken)).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    public async Task RefreshAsyncContinuesTheSessionOfTheRefreshToken()
    {
        var session = new SignInSession("session-a", DateTimeOffset.UtcNow.AddDays(30));
        var refreshToken = await IssueRefreshTokenAsync(session);

        await Assert.That(await _service.RefreshAsync(refreshToken)).IsSameReferenceAs(RefreshedTokens);
        Mock.Get(_accessTokenIssuer).IssueTokensAsync(User, Arg.Is<SignInSession?>(x => x!.Id == session.Id && x.ExpiresAt == TruncateToSeconds(session.ExpiresAt)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task RefreshAsyncRejectsARefreshTokenOfARevokedSession()
    {
        var refreshToken = await IssueRefreshTokenAsync(new("session-a", DateTimeOffset.UtcNow));
        await _sessionRevoker.RevokeAsync(new("session-a", DateTimeOffset.UtcNow));

        await Assert.That(await _service.RefreshAsync(refreshToken)).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    public async Task RefreshAsyncRejectsATokenWhenTheUserIdNoLongerExistsEvenIfTheNameWasReused()
    {
        var replacement = new User { Id = "user-b", Name = User.Name };
        Mock.Get(_userProvider).FindAsync(Arg.Is<UserFilter>(x => x.Id == User.Id), Arg.Any<CancellationToken>()).Returns((User?)null);
        Mock.Get(_userProvider).FindAsync(Arg.Is<UserFilter>(x => x.Name == User.Name), Arg.Any<CancellationToken>()).Returns(replacement);
        Mock.Get(_accessTokenIssuer).IssueTokensAsync(replacement, Arg.Any<SignInSession?>(), Arg.Any<CancellationToken>()).Returns(new IssuedTokens("access-c", "refresh-c"));

        await Assert.That(await _service.RefreshAsync(await IssueRefreshTokenAsync(new("session-a", DateTimeOffset.UtcNow)))).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task RefreshAsyncRejectsATokenWithABlankSubjectEvenWhenTheNameWasReused(string subject)
    {
        Mock.Get(_userProvider).FindAsync(Arg.Is<UserFilter>(x => x.Name == User.Name), Arg.Any<CancellationToken>()).Returns(User);

        await Assert.That(await _service.RefreshAsync(LegacyRefreshToken.CreateWithSubject(_options, User, subject))).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    public async Task RefreshAsyncRejectsATokenWithoutASubjectEvenWhenTheNameWasReused()
    {
        Mock.Get(_userProvider).FindAsync(Arg.Is<UserFilter>(x => x.Name == User.Name), Arg.Any<CancellationToken>()).Returns(User);

        await Assert.That(await _service.RefreshAsync(LegacyRefreshToken.CreateWithoutSubject(_options, User))).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    public async Task RefreshAsyncRejectsATokenWithANameIdentifierAndABlankSubject()
    {
        await Assert.That(await _service.RefreshAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _options,
            User,
            new Claim(ClaimTypes.NameIdentifier, Victim.Id),
            new Claim(JwtRegisteredClaimNames.Sub, "")))).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    public async Task RefreshAsyncRejectsATokenWithConflictingNameIdentifierAndSubject()
    {
        await Assert.That(await _service.RefreshAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _options,
            User,
            new Claim(ClaimTypes.NameIdentifier, Victim.Id),
            new Claim(JwtRegisteredClaimNames.Sub, User.Id)))).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    public async Task RefreshAsyncRejectsATokenWithTwoDifferentSubjects()
    {
        await Assert.That(await _service.RefreshAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _options,
            User,
            new Claim(JwtRegisteredClaimNames.Sub, Victim.Id),
            new Claim(JwtRegisteredClaimNames.Sub, User.Id)))).IsNull();
        await AssertNothingIssuedAsync();
    }

    [Test]
    public async Task RefreshAsyncAcceptsATokenWithMatchingSubjectAndNameIdentifier()
    {
        await Assert.That(await _service.RefreshAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _options,
            User,
            new Claim(JwtRegisteredClaimNames.Sub, User.Id),
            new Claim(ClaimTypes.NameIdentifier, User.Id)))).IsSameReferenceAs(RefreshedTokens);
    }

    [Test]
    public async Task RefreshAsyncCarriesALegacyRefreshTokenIntoASessionThatRevocationEnds()
    {
        var refreshToken = LegacyRefreshToken.Create(_options, User);
        var expectedSession = new SignInSession(SessionRevoker.GetSession(new ClaimsIdentity(), refreshToken).Id, ReadExpiry(refreshToken));

        await Assert.That(await _service.RefreshAsync(refreshToken)).IsSameReferenceAs(RefreshedTokens);
        Mock.Get(_accessTokenIssuer).IssueTokensAsync(User, expectedSession, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));

        await _sessionRevoker.RevokeAsync(expectedSession);

        await Assert.That(await _service.RefreshAsync(refreshToken)).IsNull();
    }

    private async Task<string> IssueRefreshTokenAsync(SignInSession session) =>
        (await _tokenService.IssueRefreshTokenAsync(new TokenIssuanceContext(User, [], [], []) { Session = session })).Token;

    private static DateTimeOffset ReadExpiry(string token) => new(new JsonWebTokenHandler().ReadJsonWebToken(token).ValidTo, TimeSpan.Zero);

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) => DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());

    // Both overloads: a check against only the one that is no longer called would pass without proving anything.
    private async Task AssertNothingIssuedAsync()
    {
        Mock.Get(_accessTokenIssuer).IssueTokensAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_accessTokenIssuer).IssueTokensAsync(Arg.Any<User>(), Arg.Any<SignInSession?>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }
}