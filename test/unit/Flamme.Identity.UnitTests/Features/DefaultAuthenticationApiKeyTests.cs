using System.Threading.Tasks;
using AspNetCore.Authentication.ApiKey;
using Elsa.Features.Services;
using Elsa.Identity.Features;
using Elsa.Identity.Options;
using Elsa.Identity.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Identity.UnitTests.Features;

public class DefaultAuthenticationApiKeyTests
{
    private readonly DefaultAuthenticationFeature _feature;
    private readonly ServiceCollection _services = new();

    public DefaultAuthenticationApiKeyTests()
    {
        var module = IModule.Mock().Object;
        Mock.Get(module).Services.Returns(_services);
        _feature = new DefaultAuthenticationFeature(module);
    }

    [Test]
    public async Task UsesDefaultApiKeyProviderWhenAdminApiKeyIsNotConfigured()
    {
        using var serviceProvider = Activate();

        await Assert.That(_feature.ApiKeyProviderType).IsEqualTo(typeof(DefaultApiKeyProvider));
        await Assert.That(serviceProvider.GetRequiredService<IOptions<AdminApiKeyOptions>>().Value.ApiKey).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task UsesAdminApiKeyProviderWhenAdminApiKeyIsConfigured()
    {
        _feature.UseAdminApiKey("configured-admin-api-key");

        using var serviceProvider = Activate();

        var resolvedProvider = serviceProvider.GetRequiredService<IApiKeyProvider>();
        await Assert.That(resolvedProvider).IsOfType(typeof(AdminApiKeyProvider));
        var provider = (AdminApiKeyProvider)resolvedProvider;
        var apiKey = await provider.ProvideAsync("configured-admin-api-key");

        await Assert.That(_feature.ApiKeyProviderType).IsEqualTo(typeof(AdminApiKeyProvider));
        await Assert.That(apiKey).IsNotNull();
    }

    [Test]
    public async Task UsesAdminApiKeyProviderWhenDevelopmentAdminApiKeyIsEnabled()
    {
        _feature.UseDevelopmentAdminApiKey();

        using var serviceProvider = Activate();

        var resolvedProvider = serviceProvider.GetRequiredService<IApiKeyProvider>();
        await Assert.That(resolvedProvider).IsOfType(typeof(AdminApiKeyProvider));
        var provider = (AdminApiKeyProvider)resolvedProvider;
        var apiKey = await provider.ProvideAsync(AdminApiKeyProvider.DevelopmentApiKey);

        await Assert.That(_feature.ApiKeyProviderType).IsEqualTo(typeof(AdminApiKeyProvider));
        await Assert.That(apiKey).IsNotNull();
    }

    private ServiceProvider Activate()
    {
        _feature.Apply();
        return _services.BuildServiceProvider();
    }
}
