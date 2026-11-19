using Elsa.ExternalAuthentication.Contracts;
using Elsa.ExternalAuthentication.OpenIdConnect.Features;
using Elsa.ExternalAuthentication.OpenIdConnect.Services;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Elsa.ExternalAuthentication.UnitTests.Features;

public class OpenIdConnectExternalAuthenticationFeatureTests
{
    [Test]
    public async Task ClassicFeatureRegistersTheOpenIdConnectAdapter()
    {
        var services = new ServiceCollection();
        var module = IModule.Mock().Object;
        Mock.Get(module).Services.Returns(services);
        var feature = new OpenIdConnectExternalAuthenticationFeature(module);

        feature.Apply();

        await AssertOpenIdConnectAdapterRegistered(services);
    }

    private static async Task AssertOpenIdConnectAdapterRegistered(IServiceCollection services)
    {
        await Assert.That(services).Contains(descriptor =>
            descriptor.ServiceType == typeof(IExternalAuthenticationAdapter) &&
            descriptor.ImplementationType == typeof(OpenIdConnectExternalAuthenticationAdapter));
        await Assert.That(services).Contains(descriptor =>
            descriptor.ServiceType == typeof(IAdapterSettingsMigration) &&
            descriptor.ImplementationType == typeof(OpenIdConnectSettingsV1Migration));
    }
}
