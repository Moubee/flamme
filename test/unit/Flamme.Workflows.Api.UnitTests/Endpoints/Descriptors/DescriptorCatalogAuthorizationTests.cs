using System.Net;
using Flamme.Authorization;
using Flamme.Common.Serialization;
using Flamme.Expressions.Contracts;
using Flamme.Testing.Shared.Authorization;
using Flamme.UnitTests.Shared;
using Flamme.Workflows.Api.Permissions;
using Flamme.Workflows.CommitStates;
using Flamme.Workflows.LogPersistence;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Options;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkflowsApiFeature = Flamme.Workflows.Api.Features.WorkflowsApiFeature;

namespace Flamme.Workflows.Api.UnitTests.Endpoints.Descriptors;

/// <summary>
/// The designer loads the descriptor catalogs, and a definition's versions, to open any definition, so a caller who can
/// only view definitions must be able to read them. The catalogs describe what is installed rather than anything
/// stored, so they need an authenticated caller and no grant; the version list is stored data, so it follows the
/// permission that already lets the caller read every version of a definition.
/// </summary>
[NotInParallel(nameof(FastEndpointsCollection))]
public class DescriptorCatalogAuthorizationTests
{
    private const string VersionsPath = "/workflow-definitions/my-definition/versions";
    private const string TypeName = "Elsa.Test";
    private const string OptionsPath = "/descriptors/activities/" + TypeName + "/options/Property";

    private static readonly string DefinitionsView = new Permission(WorkflowPermissions.Definitions, CoreVerbs.View).ToString();
    private static readonly string VersionsView = new Permission(WorkflowPermissions.DefinitionVersions, CoreVerbs.View).ToString();
    private static readonly string InstancesView = new Permission(WorkflowPermissions.Instances, CoreVerbs.View).ToString();
    private static readonly string DescriptorsActivitiesView = new Permission(WorkflowPermissions.DescriptorsActivities, CoreVerbs.View).ToString();

    private static readonly string[] CatalogNamespaces =
    [
        "Flamme.Workflows.Api.Endpoints.ActivityDescriptorOptions",
        "Flamme.Workflows.Api.Endpoints.ActivityDescriptors",
        "Flamme.Workflows.Api.Endpoints.CommitStrategies",
        "Flamme.Workflows.Api.Endpoints.IncidentStrategies",
        "Flamme.Workflows.Api.Endpoints.LogPersistenceStrategies",
        "Flamme.Workflows.Api.Endpoints.OutputConverters",
        "Flamme.Workflows.Api.Endpoints.Scripting.ExpressionDescriptors",
        "Flamme.Workflows.Api.Endpoints.StorageDrivers",
        "Flamme.Workflows.Api.Endpoints.VariableTypes",
        "Flamme.Workflows.Api.Endpoints.WorkflowActivationStrategies"
    ];

    private static readonly string[] CatalogPathList =
    [
        "/descriptors/activities",
        "/descriptors/activities/" + TypeName,
        "/descriptors/variables",
        "/descriptors/storage-drivers",
        "/descriptors/output-converters?sourceType=string&destinationType=string",
        "/descriptors/expression-descriptors",
        "/descriptors/workflow-activation-strategies",
        "/descriptors/incident-strategies",
        "/descriptors/log-persistence-strategies",
        "/descriptors/commit-strategies/activities",
        "/descriptors/commit-strategies/workflows"
    ];

    public static IEnumerable<string> CatalogPaths => CatalogPathList;
    public static IEnumerable<string> RefreshPermissions => [DefinitionsView, DescriptorsActivitiesView];
    public static IEnumerable<string> AuthenticatedOnlyPaths => [.. CatalogPathList, VersionsPath];

    private readonly IActivityRegistryPopulator _registryPopulator = IActivityRegistryPopulator.Mock().Object;
    private AuthorizationTestHost _host = null!;

    [Before(Test)]
    public async Task InitializeAsync() => _host = await AuthorizationTestHost.StartAsync<WorkflowsApiFeature>(
        endpoint => endpoint.Name == "ListVersions" || CatalogNamespaces.Any(x => endpoint.Namespace?.StartsWith(x, StringComparison.Ordinal) == true),
        ConfigureServices);

    [After(Test)]
    public async Task DisposeAsync()
    {
        FastEndpointsResolver.Reset();
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Test]
    [MethodDataSource(nameof(CatalogPaths))]
    public async Task Catalog_AuthenticatedUserWithoutAnyPermission_IsAllowed(string path)
    {
        var response = await SendAsync(path, authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsNotEmpty();
    }

    [Test]
    public async Task ActivityCatalog_AuthenticatedUserWithoutAnyPermission_ListsTheActivity()
    {
        var response = await SendAsync("/descriptors/activities", authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(TypeName);
    }

    [Test]
    public async Task ActivityDescriptor_AuthenticatedUserWithoutAnyPermission_ReturnsTheDescriptor()
    {
        var response = await SendAsync("/descriptors/activities/" + TypeName, authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(TypeName);
    }

    [Test]
    public async Task VersionList_WithDefinitionsView_IsAllowed()
    {
        var response = await SendAsync(VersionsPath, DefinitionsView);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsNotEmpty();
    }

    [Test]
    [MethodDataSource(nameof(AuthenticatedOnlyPaths))]
    public async Task AnonymousCaller_IsRejected(string path)
    {
        var response = await SendAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RefreshingTheActivityRegistry_WithoutAPermissionThatAllowsIt_IgnoresTheFlag()
    {
        var response = await SendAsync("/descriptors/activities?refresh=true", InstancesView);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(TypeName);
        Mock.Get(_registryPopulator).PopulateRegistryAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [MethodDataSource(nameof(RefreshPermissions))]
    public async Task RefreshingTheActivityRegistry_WithDefinitionsOrActivityDescriptorsView_Refreshes(string permission)
    {
        var response = await SendAsync("/descriptors/activities?refresh=true", permission);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(TypeName);
        Mock.Get(_registryPopulator).PopulateRegistryAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task VersionList_WithoutDefinitionsView_IsForbidden()
    {
        var response = await SendAsync(VersionsPath, InstancesView);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task VersionList_WithOnlyVersionsView_IsForbidden()
    {
        // The documented behaviour change: the versions permission alone no longer lists versions.
        var response = await SendAsync(VersionsPath, VersionsView);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ActivityPropertyOptions_WithoutActivitiesView_IsForbidden()
    {
        var response = await SendAsync(OptionsPath, DefinitionsView, method: HttpMethod.Post);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task RefreshingTheActivityRegistry_WithSecurityDisabled_RefreshesForAnyCaller()
    {
        await _host.RestartWithSecurityDisabledAsync();

        var response = await SendAsync("/descriptors/activities?refresh=true", authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Mock.Get(_registryPopulator).PopulateRegistryAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var activityRegistry = IActivityRegistry.Mock().Object;
        Mock.Get(activityRegistry).ListAll().Returns([new ActivityDescriptor { TypeName = TypeName }]);
        var activityLookup = IActivityRegistryLookupService.Mock().Object;
        Mock.Get(activityLookup).FindAsync(TypeName).Returns(new ActivityDescriptor { TypeName = TypeName });
        var expressionRegistry = IExpressionDescriptorRegistry.Mock().Object;
        Mock.Get(expressionRegistry).ListAll().Returns([]);
        var storageDrivers = IStorageDriverManager.Mock().Object;
        Mock.Get(storageDrivers).List().Returns([]);
        var logPersistence = ILogPersistenceStrategyService.Mock().Object;
        Mock.Get(logPersistence).ListStrategies().Returns([]);
        var commitStrategies = ICommitStrategyRegistry.Mock().Object;
        Mock.Get(commitStrategies).ListWorkflowStrategyRegistrations().Returns([]);
        Mock.Get(commitStrategies).ListActivityStrategyRegistrations().Returns([]);
        var definitionStore = IWorkflowDefinitionStore.Mock().Object;
        Mock.Get(definitionStore).FindManyAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<WorkflowDefinitionOrder<int>>(), Arg.Any<CancellationToken>()).Returns([new WorkflowDefinition()]);

        services
            .AddSingleton(activityRegistry)
            .AddSingleton(_registryPopulator)
            .AddSingleton(activityLookup)
            .AddSingleton(IPropertyUIHandlerResolver.Mock().Object)
            .AddSingleton(expressionRegistry)
            .AddSingleton(storageDrivers)
            .AddSingleton(logPersistence)
            .AddSingleton(commitStrategies)
            .AddSingleton(IOutputConverterRegistry.Mock().Object)
            .AddSingleton(IWellKnownTypeRegistry.Mock().Object)
            .AddSingleton(SerializationTypeRegistry.CreateDefault())
            .AddSingleton(definitionStore)
            .AddSingleton<IOptions<ManagementOptions>>(new OptionsWrapper<ManagementOptions>(new ManagementOptions()));
    }

    private Task<HttpResponseMessage> SendAsync(string path, string? permissions = null, bool authenticated = false, HttpMethod? method = null) =>
        _host.SendAsync(method ?? HttpMethod.Get, path, permissions, authenticated);
}
