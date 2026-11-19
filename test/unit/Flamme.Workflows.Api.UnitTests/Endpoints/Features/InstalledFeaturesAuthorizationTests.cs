using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Flamme.Features.Contracts;
using Flamme.Features.Models;
using Flamme.UnitTests.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowsApiFeature = Flamme.Workflows.Api.Features.WorkflowsApiFeature;

namespace Flamme.Workflows.Api.UnitTests.Endpoints.Features;

/// <summary>
/// Studio decides which modules to render from the installed features, so any signed-in user must be able to read
/// them. Requiring <c>system/features:view</c> hid every feature-gated module, including ones the caller holds
/// permissions for, from anyone without that grant.
/// </summary>
[NotInParallel(nameof(FastEndpointsCollection))]
public class InstalledFeaturesAuthorizationTests
{
    private const string FeatureFullName = "Flamme.Secrets.SecretsFeature";
    private WebApplication? _app;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This JIT-run test host intentionally discovers production endpoints through FastEndpoints reflection.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run test host intentionally discovers production endpoints through FastEndpoints reflection.")]
    [Before(Test)]
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.Assemblies = [typeof(WorkflowsApiFeature).Assembly];
            options.Filter = endpoint => endpoint.Namespace?.StartsWith("Flamme.Workflows.Api.Endpoints.Features", StringComparison.Ordinal) == true;
        });

        var feature = new FeatureDescriptor("SecretsFeature", "Flamme.Secrets", "Secrets");
        var installedFeatureProvider = IInstalledFeatureProvider.Mock().Object;
        Mock.Get(installedFeatureProvider).List().Returns([feature]);
        Mock.Get(installedFeatureProvider).Find(FeatureFullName).Returns(feature);
        builder.Services.AddSingleton(installedFeatureProvider);

        builder.Services
            .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseFastEndpoints();
        await _app.StartAsync();
    }

    [After(Test)]
    public async Task DisposeAsync()
    {
        FastEndpointsResolver.Reset();
        if (_app is null)
            return;

        try
        {
            await _app.StopAsync();
        }
        finally
        {
            await _app.DisposeAsync();
        }
    }

    [Test]
    [Arguments("/features/installed")]
    [Arguments("/features/installed/" + FeatureFullName)]
    public async Task AuthenticatedUserWithoutAnyPermission_IsAllowed(string path)
    {
        var response = await SendAsync(path, authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(FeatureFullName);
    }

    [Test]
    [Arguments("/features/installed")]
    [Arguments("/features/installed/" + FeatureFullName)]
    public async Task AnonymousCaller_IsRejected(string path)
    {
        var response = await SendAsync(path, authenticated: false);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> SendAsync(string path, bool authenticated)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (authenticated)
        {
            request.Headers.Add(HeaderAuthenticationHandler.HeaderName, "user-without-grants");
        }

        return _app!.GetTestClient().SendAsync(request);
    }

    /// <summary>
    /// Authenticates a caller that names itself in a header, with no permission claims at all; a request without the
    /// header stays anonymous.
    /// </summary>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Header";
        public const string HeaderName = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], SchemeName);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
