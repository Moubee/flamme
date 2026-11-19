using Microsoft.Extensions.Time.Testing;
using Flamme.Authorization;
using System.Net;
using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Permissions;
using Flamme.ExternalAuthentication.Services;
using Flamme.Common;
using Flamme.Common.Multitenancy;
using Flamme.Common.Services;
using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Providers;
using Flamme.Identity.Services;
using Flamme.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Flamme.ExternalAuthentication.IntegrationTests.Fixtures;
using TUnit.AspNetCore;

namespace Flamme.ExternalAuthentication.IntegrationTests.Links;

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
        services.AddSingleton<IIdentityProviderConnectionRegistry>(IIdentityProviderConnectionRegistry.Mock().Object);
        var tenant = ITenantAccessor.Mock().Object;
        Mock.Get(tenant).TenantId.Returns("tenant-a");
        services.AddSingleton(tenant);
        services.AddScoped<IUserStore, MemoryUserStore>();
        services.AddScoped<IUserProvider, StoreBasedUserProvider>();
        services.AddSingleton<IRoleProvider>(IRoleProvider.Mock().Object);
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
