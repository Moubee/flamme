using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Elsa.Common;
using Elsa.Common.Services;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Identity.Contracts;
using Elsa.Identity.Entities;
using Elsa.Identity.Features;
using Elsa.Identity.HostedServices;
using Elsa.Identity.Models;
using Elsa.Identity.Options;
using Elsa.UnitTests.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;

namespace Elsa.Identity.UnitTests.Endpoints;

/// <summary>
/// Refresh-token exchange through the real endpoint, authentication scheme and token issuer.
/// </summary>
[NotInParallel(nameof(FastEndpointsCollection))]
public sealed class RefreshTokenEndpointTests
{
    private static readonly User Alice = new() { Id = "alice-id", Name = "alice" };
    private static readonly User Victim = new() { Id = "victim-id", Name = "victim" };
    private readonly MutableSystemClock _clock = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private IdentityTokenOptions _tokenOptions = null!;
    private MemoryStore<User> _users = null!;

    [Before(Test)]
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var module = Substitute.For<IModule>();
        module.Services.Returns(builder.Services);
        new IdentityFeature(module) { TokenOptions = options => options.SigningKey = IdentityTokenTestConstants.SigningKey }.Apply();
        new DefaultAuthenticationFeature(module).Apply();

        // The startup diagnostics need the permission catalog, which this host does not install.
        foreach (var diagnostic in builder.Services.Where(x => x.ImplementationType?.Namespace == typeof(StoredPermissionValidator).Namespace).ToList())
        {
            builder.Services.Remove(diagnostic);
        }

        builder.Services
            .AddSingleton<ISystemClock>(_clock)
            .AddScoped<IUserCredentialsValidator, NameOnlyCredentialsValidator>()
            .AddFastEndpoints(options =>
            {
                options.Assemblies = [typeof(IdentityFeature).Assembly];
                options.Filter = x => x.Namespace is "Elsa.Identity.Endpoints.Login" or "Elsa.Identity.Endpoints.RefreshToken";
            });

        _app = builder.Build();
        _tokenOptions = _app.Services.GetRequiredService<IOptions<IdentityTokenOptions>>().Value;
        _users = _app.Services.GetRequiredService<MemoryStore<User>>();
        _users.Save(Alice, x => x.Id);
        _users.Save(Victim, x => x.Id);
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseFastEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [After(Test)]
    public async Task DisposeAsync()
    {
        try
        {
            _client?.Dispose();
            if (_app is not null)
            {
                try
                {
                    await _app.StopAsync();
                }
                finally
                {
                    await _app.DisposeAsync();
                }
            }
        }
        finally
        {
            FastEndpointsResolver.Reset();
        }
    }

    [Test]
    public async Task ANormalRefreshStillWorks()
    {
        var tokens = await LoginAsync(Alice);

        var refreshed = await RefreshTokensAsync(tokens.RefreshToken);

        await Assert.That(string.IsNullOrWhiteSpace(refreshed.AccessToken)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(refreshed.RefreshToken)).IsFalse();
        await Assert.That(ReadClaim(refreshed.RefreshToken, JwtRegisteredClaimNames.Sub)).IsEqualTo(Alice.Id);
        await Assert.That(ReadClaim(refreshed.RefreshToken, JwtRegisteredClaimNames.Name)).IsEqualTo(Alice.Name);
    }

    [Test]
    public async Task ARefreshTokenIsRejectedWhenTheUserIsDeletedAndRecreatedWithTheSameName()
    {
        var tokens = await LoginAsync(Alice);
        var replacement = new User { Id = "alice-id-2", Name = Alice.Name };

        _users.Delete(Alice.Id);
        _users.Save(replacement, x => x.Id);

        await AssertRefreshRejectedAsync(tokens.RefreshToken);

        var replacementTokens = await LoginAsync(replacement);
        var refreshed = await RefreshTokensAsync(replacementTokens.RefreshToken);
        await Assert.That(ReadClaim(refreshed.RefreshToken, JwtRegisteredClaimNames.Sub)).IsEqualTo(replacement.Id);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task ARefreshTokenWithABlankSubjectIsRejectedEvenWhenASameNameUserExists(string subject)
    {
        await AssertRefreshRejectedAsync(LegacyRefreshToken.CreateWithSubject(_tokenOptions, Alice, subject));
    }

    [Test]
    public async Task ARefreshTokenWithoutASubjectIsRejectedEvenWhenASameNameUserExists()
    {
        await AssertRefreshRejectedAsync(LegacyRefreshToken.CreateWithoutSubject(_tokenOptions, Alice));
    }

    [Test]
    public async Task ARefreshTokenWithANameIdentifierAndABlankSubjectIsRejected()
    {
        await AssertRefreshRejectedAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _tokenOptions,
            Alice,
            new Claim(ClaimTypes.NameIdentifier, Victim.Id),
            new Claim(JwtRegisteredClaimNames.Sub, "")));
    }

    [Test]
    public async Task ARefreshTokenWithConflictingNameIdentifierAndSubjectIsRejected()
    {
        await AssertRefreshRejectedAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _tokenOptions,
            Alice,
            new Claim(ClaimTypes.NameIdentifier, Victim.Id),
            new Claim(JwtRegisteredClaimNames.Sub, Alice.Id)));
    }

    [Test]
    public async Task ARefreshTokenWithTwoDifferentSubjectsIsRejected()
    {
        await AssertRefreshRejectedAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _tokenOptions,
            Alice,
            new Claim(JwtRegisteredClaimNames.Sub, Victim.Id),
            new Claim(JwtRegisteredClaimNames.Sub, Alice.Id)));
    }

    [Test]
    public async Task ARefreshTokenWithMatchingSubjectAndNameIdentifierStillWorks()
    {
        var refreshed = await RefreshTokensAsync(LegacyRefreshToken.CreateWithSubjectClaims(
            _tokenOptions,
            Alice,
            new Claim(JwtRegisteredClaimNames.Sub, Alice.Id),
            new Claim(ClaimTypes.NameIdentifier, Alice.Id)));

        await Assert.That(ReadClaim(refreshed.RefreshToken, JwtRegisteredClaimNames.Sub)).IsEqualTo(Alice.Id);
    }

    private async Task<IssuedTokens> LoginAsync(User user) =>
        await ReadTokensAsync(await _client.PostAsJsonAsync("/identity/login", new { username = user.Name, password = "unchecked" }));

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/identity/refresh-token");
        request.Headers.Authorization = new("Bearer", refreshToken);
        return await _client.SendAsync(request);
    }

    private async Task<IssuedTokens> RefreshTokensAsync(string refreshToken) => await ReadTokensAsync(await RefreshAsync(refreshToken));

    private async Task AssertRefreshRejectedAsync(string refreshToken) => await Assert.That((await RefreshAsync(refreshToken)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

    private static string? ReadClaim(string token, string claimType) =>
        new JsonWebTokenHandler().ReadJsonWebToken(token).Claims.FirstOrDefault(x => x.Type == claimType)?.Value;

    private static async Task<IssuedTokens> ReadTokensAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        await Assert.That(body.IsAuthenticated).IsTrue();
        return new(body.AccessToken!, body.RefreshToken!);
    }

    // Password verification is not under test, and costs 600,000 PBKDF2 iterations per sign-in.
    private sealed class NameOnlyCredentialsValidator(IUserProvider userProvider) : IUserCredentialsValidator
    {
        public async ValueTask<User?> ValidateAsync(string username, string password, CancellationToken cancellationToken = default) =>
            await userProvider.FindByNameAsync(username, cancellationToken);
    }
}
