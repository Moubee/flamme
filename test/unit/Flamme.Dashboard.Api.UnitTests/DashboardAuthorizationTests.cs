using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flamme.Authorization;
using Flamme.Dashboard.Abstractions.Contracts;
using Flamme.Dashboard.Abstractions.Models;
using Flamme.Dashboard.Api.Authorization;
using Flamme.Dashboard.Api.Permissions;
using Flamme.Dashboard.Api.Services;
using Flamme.Testing.Shared.Authorization;
using Flamme.Workflows.Api.Permissions;
using Microsoft.Extensions.DependencyInjection;
using DashboardApiFeature = Flamme.Dashboard.Api.Features.DashboardApiFeature;
using static Flamme.Dashboard.Api.UnitTests.TestContributor;

namespace Flamme.Dashboard.Api.UnitTests;

/// <summary>
/// Elsa Studio shows the dashboard to every signed-in user and gates each widget by the permission of the data it shows,
/// so the API applies the same rule: <c>dashboard:view</c> reads everything, a narrower permission reads just its own
/// data, and the overview never refuses a caller who can read part of it.
/// </summary>
[NotInParallel(nameof(EndpointSecurityCollection))]
public class DashboardAuthorizationTests
{
    private static readonly string WholeOverviewPermission = Format(WholeOverview);
    private static readonly string InstancesPermission = Format(InstancesView);

    private static readonly (HttpMethod Method, string Path)[] InstanceEndpointList =
    [
        (HttpMethod.Post, "/dashboard/workflow-trends"),
        (HttpMethod.Get, "/dashboard/recent-activity"),
        (HttpMethod.Get, "/dashboard/needs-attention"),
        (HttpMethod.Post, "/dashboard/workflow-hotspots")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly (HttpMethod Method, string Path) OverviewEndpoint = (HttpMethod.Get, "/dashboard/overview");

    public static IEnumerable<(string, string)> InstanceEndpoints() => InstanceEndpointList.Select(x => (x.Method.Method, x.Path));
    public static IEnumerable<(string, string)> AllEndpoints() => InstanceEndpointList.Append(OverviewEndpoint).Select(x => (x.Method.Method, x.Path));

    public static IEnumerable<Type> InstanceEndpointTypes() =>
    [
        typeof(Endpoints.Dashboard.WorkflowTrends.Endpoint),
        typeof(Endpoints.Dashboard.RecentActivity.Endpoint),
        typeof(Endpoints.Dashboard.NeedsAttention.Endpoint),
        typeof(Endpoints.Dashboard.WorkflowHotspots.Endpoint)
    ];

    /// <summary>Every instance endpoint crossed with each permission that reads instance data.</summary>
    public static IEnumerable<(string, string, string)> InstanceEndpointsWithInstanceAccess() =>
        from endpoint in InstanceEndpointList
        from permission in new[] { InstancesPermission, WholeOverviewPermission }
        select (endpoint.Method.Method, endpoint.Path, permission);

    /// <summary>Every instance endpoint crossed with each permission that guards only another section.</summary>
    public static IEnumerable<(string, string, string)> InstanceEndpointsWithAnotherSectionsPermission() =>
        from endpoint in InstanceEndpointList
        from permission in new[] { RuntimeView }.Select(Format)
        select (endpoint.Method.Method, endpoint.Path, permission);

    /// <summary>The sections a caller holding only the permission reads; every other section is withheld.</summary>
    public static IEnumerable<(string, SectionAccess)> ReadableSections() => SectionAccess.BySinglePermission.Select(x => (Format(x.Permission), x.Readable))
        .Concat([
            (WholeOverviewPermission, SectionAccess.All),
            ("workflows/*:view", new SectionAccess { Runtime = true, Instances = true }),
            ("*:view", SectionAccess.All)
        ]);

    private AuthorizationTestHost _host = null!;

    [Before(Test)]
    public async Task InitializeAsync() => _host = await AuthorizationTestHost.StartAsync<DashboardApiFeature>(
        endpoint => endpoint.Namespace?.StartsWith("Flamme.Dashboard.Api.Endpoints", StringComparison.Ordinal) == true,
        services => services.AddSingleton<IDashboardProvider>(new DefaultDashboardProvider([Declared()], new(new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero))), new TestHostEnvironment())));

    [After(Test)]
    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Test]
    [MethodDataSource(nameof(AllEndpoints))]
    public async Task AnonymousCaller_IsRejected(string method, string path)
    {
        var response = await SendAsync(new(method), path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [MethodDataSource(nameof(ReadableSections))]
    public async Task Overview_WithOnePermission_ReturnsOnlyTheSectionsItGuards(string permission, SectionAccess readable)
    {
        var response = await SendAsync(HttpMethod.Get, OverviewEndpoint.Path, permission);
        var overview = await ReadOverviewAsync(response);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await readable.AssertOn(overview);
    }

    [Test]
    public async Task Overview_WithAnyAuthenticatedCallerWithoutPermissions_WithholdsEverySection()
    {
        var response = await SendAsync(HttpMethod.Get, OverviewEndpoint.Path, authenticated: true);
        var overview = await ReadOverviewAsync(response);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await SectionAccess.None.AssertOn(overview);
        await Assert.That(overview.BackendName).IsNull();
        await Assert.That(overview.EnvironmentName).IsNull();
        await Assert.That(overview.Metrics).IsEmpty();
        await Assert.That(overview.Panels).IsEmpty();
    }

    [Test]
    public async Task Overview_WithInstancesView_ReturnsTheMetricsAndPanelsBuiltFromInstances()
    {
        var overview = await ReadOverviewAsync(await SendAsync(HttpMethod.Get, OverviewEndpoint.Path, InstancesPermission));

        await Assert.That(overview.Metrics.Select(x => x.Id)).IsEquivalentTo([InstancesMetric], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(overview.Panels.Select(x => x.Id)).IsEquivalentTo([InstancesPanel], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Overview_WithDashboardView_ReturnsEverything()
    {
        var overview = await ReadOverviewAsync(await SendAsync(HttpMethod.Get, OverviewEndpoint.Path, WholeOverviewPermission));

        await Assert.That(overview.Metrics.Count).IsEqualTo(3);
        await Assert.That(overview.Panels.Count).IsEqualTo(3);
    }

    [Test]
    [MethodDataSource(nameof(InstanceEndpointsWithInstanceAccess))]
    public async Task InstanceEndpoint_WithInstancesViewOrDashboardView_IsAllowed(string method, string path, string permission)
    {
        var response = await SendAsync(new(method), path, permission);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsNotEmpty();
    }

    [Test]
    [MethodDataSource(nameof(InstanceEndpointsWithAnotherSectionsPermission))]
    public async Task InstanceEndpoint_WithAPermissionOnlyForAnotherSection_IsForbidden(string method, string path, string permission)
    {
        var response = await SendAsync(new(method), path, permission);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    [MethodDataSource(nameof(InstanceEndpoints))]
    public async Task InstanceEndpoint_WithoutAnyPermission_IsForbidden(string method, string path)
    {
        var response = await SendAsync(new(method), path, authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task NeedsAttention_WithInstancesView_ReturnsOnlyTheFindingsBuiltFromInstances()
    {
        var response = await SendAsync(HttpMethod.Get, "/dashboard/needs-attention", InstancesPermission);
        var findings = (await response.Content.ReadFromJsonAsync(new TestJsonSerializerContext(new JsonSerializerOptions(JsonOptions)).DashboardNeedsAttentionResponse))!.Findings;

        await Assert.That(findings.Select(x => x.Id)).IsEquivalentTo([InstancesFinding], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [MethodDataSource(nameof(AllEndpoints))]
    public async Task CallerWithoutPermissions_WithSecurityDisabled_IsAllowedEverything(string method, string path)
    {
        await _host.RestartWithSecurityDisabledAsync();

        var response = await SendAsync(new(method), path, authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Overview_WithSecurityDisabled_ReturnsEverything()
    {
        await _host.RestartWithSecurityDisabledAsync();

        var overview = await ReadOverviewAsync(await SendAsync(HttpMethod.Get, OverviewEndpoint.Path, authenticated: true));

        await SectionAccess.All.AssertOn(overview);
        await Assert.That(overview.Metrics.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("GET", "/dashboard/overview", "metrics")]
    [Arguments("GET", "/dashboard/needs-attention", "findings")]
    [Arguments("POST", "/dashboard/workflow-trends", "buckets")]
    [Arguments("GET", "/dashboard/recent-activity", "items")]
    [Arguments("POST", "/dashboard/workflow-hotspots", "items")]
    public async Task Response_NeverSerializesThePermissionsThatGuardIt(string method, string path, string collection)
    {
        var response = await SendAsync(new(method), path, WholeOverviewPermission);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(json.RootElement.GetProperty(collection).EnumerateArray()).IsNotEmpty();
        await Assert.That(json.RootElement.GetRawText()).DoesNotContain("permission", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    [MethodDataSource(nameof(InstanceEndpointTypes))]
    public async Task InstanceEndpoint_RecordsBothPermissionsThatOpenIt(Type endpointType)
    {
        var requirement = EndpointPermissionRegistry.FindRequirement(endpointType);

        await Assert.That(requirement).IsNotNull();
        await Assert.That(requirement.AnyOf).IsEquivalentTo(new[] { new Permission(DashboardResourcePermissions.Dashboard, CoreVerbs.View), new Permission(WorkflowPermissions.Instances, CoreVerbs.View) }, EqualityComparer<Permission>.Default);
    }

    [Test]
    public async Task DashboardAccess_PinsTheInstancesPermissionToTheWorkflowsModule()
    {
        await Assert.That(DashboardAccess.WorkflowInstances).IsEqualTo(new DashboardPermission(WorkflowPermissions.Instances, CoreVerbs.View));
    }

    private static string Format(DashboardPermission permission) => new Permission(permission.Resource, permission.Verb).ToString();

    private static async Task<DashboardOverview> ReadOverviewAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync(new TestJsonSerializerContext(new JsonSerializerOptions(JsonOptions)).DashboardOverview))!;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? permissions = null, bool authenticated = false) =>
        _host.SendAsync(method, path, permissions, authenticated, method == HttpMethod.Post ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json") : null);
}

public class EndpointSecurityCollection;
