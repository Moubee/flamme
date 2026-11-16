using System.Net;
using System.Reflection;
using Elsa.Abstractions;
using Elsa.Authorization;
using Elsa.Testing.Shared.Authorization;
using FastEndpoints;

namespace Elsa.Api.Common.UnitTests.Abstractions;

/// <summary>
/// An endpoint declaring <c>RequireAnyPermission</c> opens to a caller holding any one of the permissions it names, and is
/// otherwise enforced, recorded and switched off exactly as <c>RequirePermission</c> is.
/// </summary>
[NotInParallel(nameof(EndpointSecurityCollection))]
public class RequireAnyPermissionTests
{
    private static readonly Permission Alpha = new("tests/alpha", CoreVerbs.View);
    private static readonly Permission Beta = new("tests/beta", CoreVerbs.View);

    /// <summary>Grants satisfying one of the two permissions, directly or through a wildcard.</summary>
    public static IEnumerable<string> SatisfyingGrants() => [Alpha.ToString(), Beta.ToString(), "tests/*:view", "*:view", "tests/beta:*", "*"];

    /// <summary>Grants close to one of the two permissions that satisfy neither.</summary>
    public static IEnumerable<string> UnsatisfyingGrants() => ["tests/gamma:view", "tests/alpha:delete", "tests/alpha/child:view", "*:delete"];

    private AuthorizationTestHost _host = null!;

    [Before(Test)]
    public async Task InitializeAsync() => _host = await AuthorizationTestHost.StartAsync<AnyOfEndpoint>(endpoint => endpoint == typeof(AnyOfEndpoint));

    [After(Test)]
    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Test]
    [MethodDataSource(nameof(SatisfyingGrants))]
    public async Task Caller_HoldingAGrantThatSatisfiesEitherPermission_IsAllowed(string grant)
    {
        var response = await SendAsync(grant);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [MethodDataSource(nameof(UnsatisfyingGrants))]
    public async Task Caller_HoldingAGrantThatSatisfiesNeitherPermission_IsForbidden(string grant)
    {
        var response = await SendAsync(grant);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Caller_HoldingNoPermission_IsForbidden()
    {
        var response = await SendAsync(authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnonymousCaller_IsChallenged()
    {
        var response = await SendAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AnonymousCaller_WithSecurityDisabled_IsAllowed()
    {
        await _host.RestartWithSecurityDisabledAsync();

        var response = await SendAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Declaration_IsRecordedAsOneRequirementNamingBothPermissions()
    {
        var requirement = EndpointPermissionRegistry.FindRequirement(typeof(AnyOfEndpoint));

        await Assert.That(requirement).IsNotNull();
        await Assert.That(requirement.AnyOf).IsEquivalentTo([Alpha, Beta], EqualityComparer<Permission>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(EndpointPermissionRegistry.Find(typeof(AnyOfEndpoint))).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void DeclaringNoPermissions_IsRejected_WhetherOrNotSecurityIsEnabled(bool securityIsEnabled)
    {
        // The empty declaration is rejected before the security check, so a host running without security still surfaces the mistake.
        var wasSecurityEnabled = EndpointSecurityOptions.SecurityIsEnabled;
        EndpointSecurityOptions.SecurityIsEnabled = securityIsEnabled;

        try
        {
            var endpoint = new EmptyDeclarationEndpoint();
            typeof(EmptyDeclarationEndpoint)
                .GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(endpoint, new EndpointDefinition(typeof(EmptyDeclarationEndpoint), typeof(EmptyRequest), typeof(string)));

            Assert.ThrowsExactly<ArgumentException>(endpoint.Configure);
        }
        finally
        {
            EndpointSecurityOptions.SecurityIsEnabled = wasSecurityEnabled;
        }
    }

    private Task<HttpResponseMessage> SendAsync(string? permissions = null, bool authenticated = false) =>
        _host.SendAsync(HttpMethod.Get, AnyOfEndpoint.Route, permissions, authenticated);

    public sealed class AnyOfEndpoint : ElsaEndpointWithoutRequest<string>
    {
        public const string Route = "/tests/any-of";

        public override void Configure()
        {
            Get(Route);
            RequireAnyPermission((Alpha.Resource, Alpha.Verb), (Beta.Resource, Beta.Verb));
        }

        public override Task<string> ExecuteAsync(CancellationToken ct) => Task.FromResult("ok");
    }

    public sealed class EmptyDeclarationEndpoint : ElsaEndpointWithoutRequest<string>
    {
        public override void Configure()
        {
            Get("/tests/empty");
            RequireAnyPermission();
        }

        public override Task<string> ExecuteAsync(CancellationToken ct) => Task.FromResult("ok");
    }
}

public class EndpointSecurityCollection;
