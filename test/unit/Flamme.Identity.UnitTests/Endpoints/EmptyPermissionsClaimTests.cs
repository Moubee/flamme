using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa;
using Elsa.Authorization;
using Elsa.Common.Services;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Identity.Contracts;
using Elsa.Identity.Entities;
using Elsa.Identity.Features;
using Elsa.Identity.HostedServices;
using Elsa.Identity.Models;
using Elsa.Identity.Permissions;
using Elsa.UnitTests.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Identity.UnitTests.Endpoints;

/// <summary>
/// A zero-grant user's Elsa tokens must carry a surviving <c>permissions</c> claim so Studio can tell
/// "known empty" from "unknown", while gated endpoints stay 403.
/// </summary>
[NotInParallel(nameof(FastEndpointsCollection))]
public sealed class EmptyPermissionsClaimTests
{
    private static readonly User NoPerm = new() { Id = "noperm-id", Name = "noperm", Roles = ["noperm"] };
    private static readonly User Reader = new() { Id = "reader-id", Name = "reader", Roles = ["reader"] };
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This JIT-run test host intentionally discovers production endpoints through FastEndpoints reflection.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run test host intentionally discovers production endpoints through FastEndpoints reflection.")]
    [Before(Test)]
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var module = IModule.Mock().Object;
        Mock.Get(module).Services.Returns(builder.Services);
        new IdentityFeature(module) { TokenOptions = options => options.SigningKey = IdentityTokenTestConstants.SigningKey }.Apply();
        new DefaultAuthenticationFeature(module).Apply();

        foreach (var diagnostic in builder.Services.Where(x => x.ImplementationType?.Namespace == typeof(StoredPermissionValidator).Namespace).ToList())
        {
            builder.Services.Remove(diagnostic);
        }

        builder.Services
            .AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow))
            .AddElsaAuthorization()
            .AddPermissionDescriptors<IdentityPermissionsDescriptorProvider>()
            .AddScoped<IUserCredentialsValidator, NameOnlyCredentialsValidator>()
            .AddFastEndpoints(options =>
            {
                options.Assemblies = [typeof(IdentityFeature).Assembly];
                options.Filter = x => x.Namespace is "Elsa.Identity.Endpoints.Login"
                    or "Elsa.Identity.Endpoints.RefreshToken"
                    or "Elsa.Identity.Endpoints.Me.Permissions"
                    or "Elsa.Identity.Endpoints.Users.List";
            });

        _app = builder.Build();
        _app.Services.GetRequiredService<MemoryStore<User>>().Save(NoPerm, x => x.Id);
        _app.Services.GetRequiredService<MemoryStore<User>>().Save(Reader, x => x.Id);
        _app.Services.GetRequiredService<MemoryStore<Role>>().Save(new Role { Id = "noperm", Name = "noperm" }, x => x.Id);
        _app.Services.GetRequiredService<MemoryStore<Role>>().Save(new Role { Id = "reader", Name = "reader", Permissions = ["identity/users:view"] }, x => x.Id);
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
    public async Task LoginIssuesTheEmptySetSentinelForAZeroGrantUser()
    {
        var tokens = await LoginAsync(NoPerm);

        await AssertKnownEmptySentinel(tokens.AccessToken);
        await AssertKnownEmptySentinel(tokens.RefreshToken);
    }

    [Test]
    public async Task RefreshReissuesTheEmptySetSentinel()
    {
        var refreshed = await RefreshTokensAsync((await LoginAsync(NoPerm)).RefreshToken);

        await AssertKnownEmptySentinel(refreshed.AccessToken);
        await AssertKnownEmptySentinel(refreshed.RefreshToken);
    }

    [Test]
    public async Task AGrantedUserStillReceivesTheirPermissions()
    {
        var token = (await LoginAsync(Reader)).AccessToken;
        var payload = StudioWasmJwtParser.ReadPayload(token);

        await Assert.That(payload.GetProperty(PermissionNames.ClaimType).GetString()).IsEqualTo("identity/users:view");
        await Assert.That(StudioWasmJwtParser.Parse(token)).DoesNotContain(x => x.Type == PermissionNames.ClaimType && x.Value == PermissionNames.None);
    }

    [Test]
    public async Task AZeroGrantUserIsForbiddenOnGatedEndpoints()
    {
        var tokens = await LoginAsync(NoPerm);

        await Assert.That((await SendAuthorizedAsync(HttpMethod.Get, "/identity/users", tokens.AccessToken)).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That((await SendAuthorizedAsync(HttpMethod.Get, "/identity/users", (await LoginAsync(Reader)).AccessToken)).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task MePermissionsReportsNoVerbsForAZeroGrantUser()
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/identity/me/permissions", (await LoginAsync(NoPerm)).AccessToken);
        var body = await response.Content.ReadFromJsonAsync(TestJsonSerializerContext.Default.MePermissionsResponse);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body.Grants).Contains(x => x.Resource == IdentityPermissions.Users);
        foreach (var grant in body.Grants)
        {
            await Assert.That(grant.Verbs).IsEmpty();
        }
    }

    [Test]
    public async Task MePermissionsReportsHeldVerbsForAGrantedUser()
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/identity/me/permissions", (await LoginAsync(Reader)).AccessToken);
        var body = await response.Content.ReadFromJsonAsync(TestJsonSerializerContext.Default.MePermissionsResponse);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.Grants).Contains(x => x.Resource == IdentityPermissions.Users && x.Verbs.Contains(CoreVerbs.View));
    }
    private static async Task AssertKnownEmptySentinel(string token)
    {
        var payload = StudioWasmJwtParser.ReadPayload(token);
        await Assert.That(payload.GetProperty(PermissionNames.ClaimType).ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(payload.GetProperty(PermissionNames.ClaimType).GetString()).IsEqualTo(PermissionNames.None);

        var wasmClaims = StudioWasmJwtParser.Parse(token).Where(x => x.Type == PermissionNames.ClaimType).ToList();
        await Assert.That(wasmClaims).IsNotEmpty();
        foreach (var claim in wasmClaims)
        {
            await Assert.That(Permission.TryParse(claim.Value, out _)).IsFalse();
        }
    }

    private async Task<IssuedTokens> LoginAsync(User user) =>
        await ReadTokensAsync(await _client.PostAsync("/identity/login", new StringContent(new JsonObject { ["username"] = user.Name, ["password"] = "unchecked" }.ToJsonString(), System.Text.Encoding.UTF8, "application/json")));

    private async Task<IssuedTokens> RefreshTokensAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/identity/refresh-token");
        request.Headers.Authorization = new("Bearer", refreshToken);
        return await ReadTokensAsync(await _client.SendAsync(request));
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, string accessToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private static async Task<IssuedTokens> ReadTokensAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync(TestJsonSerializerContext.Default.LoginResponse))!;
        await Assert.That(body.IsAuthenticated).IsTrue();
        return new(body.AccessToken!, body.RefreshToken!);
    }

    internal sealed record MePermissionsResponse(IReadOnlyCollection<MeResourceGrant> Grants);

    internal sealed record MeResourceGrant(string Resource, IReadOnlyCollection<string> Verbs);

    private sealed class NameOnlyCredentialsValidator(IUserProvider userProvider) : IUserCredentialsValidator
    {
        public async ValueTask<User?> ValidateAsync(string username, string password, CancellationToken cancellationToken = default) =>
            await userProvider.FindByNameAsync(username, cancellationToken);
    }
}
