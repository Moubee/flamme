using Microsoft.Extensions.Time.Testing;
using Elsa.Authorization;
using System.Net;
using Elsa.ExternalAuthentication.Contracts;
using Elsa.ExternalAuthentication.Permissions;
using Elsa.ExternalAuthentication.Services;
using Elsa.Common;
using Elsa.Common.Multitenancy;
using Elsa.Common.Services;
using Elsa.Identity.Contracts;
using Elsa.Identity.Entities;
using Elsa.Identity.Providers;
using Elsa.Identity.Services;
using Elsa.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Elsa.ExternalAuthentication.IntegrationTests.Fixtures;
using TUnit.AspNetCore;

namespace Elsa.ExternalAuthentication.IntegrationTests.Links;

public class IdentityLinkAuthorizationTests : WebApplicationTest<IdentityLinkAuthorizationWebApplicationFactory, ExternalAuthenticationTestEntryPoint>
{
    private HttpClient? _client;
    private readonly TestAuthenticationState _authentication = new();

    private HttpClient Client => _client ??= Factory.CreateClient();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        _authentication.SetPermissions($"{ExternalAuthenticationResourcePermissions.Connections}:{CoreVerbs.View}");
        services.AddSingleton(_authentication);
        services.AddSingleton<MemoryStore<User>>();
        services.AddSingleton<IIdentityGenerator, GuidIdentityGenerator>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
        services.AddSingleton<IExternalAuthenticationHandleHasher, HmacExternalAuthenticationHandleHasher>();
        services.AddSingleton<InMemoryExternalIdentityProvisionerState>();
        services.AddSingleton<IIdentityProviderConnectionRegistry>(Mock.Of<IIdentityProviderConnectionRegistry>().Object);
        var tenant = Mock.Of<ITenantAccessor>().Object;
        Mock.Get(tenant).TenantId.Returns("tenant-a");
        services.AddSingleton(tenant);
        services.AddScoped<IUserStore, MemoryUserStore>();
        services.AddScoped<IUserProvider, StoreBasedUserProvider>();
        services.AddSingleton<IRoleProvider>(Mock.Of<IRoleProvider>().Object);
        services.AddScoped<InMemoryExternalIdentityProvisioner>();
        services.AddScoped<IExternalIdentityProvisioner>(provider => provider.GetRequiredService<InMemoryExternalIdentityProvisioner>());
        services.AddScoped<IExternalIdentityLinkManagementStore>(provider => provider.GetRequiredService<InMemoryExternalIdentityProvisioner>());
        services.AddScoped<ExternalIdentityLinkManagementService>();
    }

    [Test]
    public async Task UserOptionsRequiresTheLinkManagementPermissionRatherThanAnUnrelatedPermission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/external-authentication/user-options");
        await Assert.That((await Client.SendAsync(request)).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
}
