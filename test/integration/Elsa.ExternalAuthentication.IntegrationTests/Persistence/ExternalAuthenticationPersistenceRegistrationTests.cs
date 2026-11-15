using Elsa.ExternalAuthentication.Contracts;
using Elsa.ExternalAuthentication.Persistence.EFCore.Features;
using Elsa.ExternalAuthentication.Persistence.EFCore.PostgreSql.Extensions;
using Elsa.ExternalAuthentication.Persistence.EFCore.Stores;
using Elsa.ExternalAuthentication.Services;
using Elsa.ExternalAuthentication.Stores.InMemory;
using Elsa.Features.Services;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.ExternalAuthentication.IntegrationTests.Persistence;

public class ExternalAuthenticationPersistenceRegistrationTests
{
    [Test]
    public async Task PersistenceRegistrationSuppliesTheDefaultHandleHasher()
    {
        var services = new ServiceCollection();
        services.AddExternalAuthenticationEntityFrameworkCore();

        using var serviceProvider = services.BuildServiceProvider();

        await Assert.That(
            serviceProvider.GetRequiredService<IExternalAuthenticationHandleHasher>()).IsOfType(typeof(HmacExternalAuthenticationHandleHasher));
    }

    [Test]
    public async Task PostgreSqlIdentityFeatureDoesNotRegisterExternalAuthenticationPersistence()
    {
        var services = new ServiceCollection();
        services.AddExternalAuthenticationServices();

        var module = Mock.Of<IModule>().Object;
        Mock.Get(module).Services.Returns(services);
        var feature = new EFCoreIdentityPersistenceFeature(module);
        feature.UsePostgreSql("Host=localhost;Database=elsa;Username=elsa;Password=elsa");
        feature.Apply();

        // External authentication persistence has its own feature; enabling identity persistence must not imply it.
        var registration = services.Last(x => x.ServiceType == typeof(IIdentityProviderConnectionStore));

        await Assert.That(registration.ImplementationType).IsEqualTo(typeof(InMemoryIdentityProviderConnectionStore));
    }

    [Test]
    public async Task PostgreSqlExternalAuthenticationFeatureRegistersEntityFrameworkCoreStores()
    {
        var services = new ServiceCollection();
        services.AddExternalAuthenticationServices();

        var feature = CreatePersistenceFeature(services);
        feature.Apply();

        var connectionStore = services.Last(x => x.ServiceType == typeof(IIdentityProviderConnectionStore));
        await Assert.That(connectionStore.ImplementationType).IsEqualTo(typeof(EFCoreIdentityProviderConnectionStore));
        await Assert.That(connectionStore.Lifetime).IsEqualTo(ServiceLifetime.Singleton);

        var sessionStore = services.Last(x => x.ServiceType == typeof(IExternalAuthenticationSessionStore));
        await Assert.That(sessionStore.ImplementationType).IsEqualTo(typeof(EFCoreExternalAuthenticationSessionStore));
    }

    [Test]
    public async Task StoresConsumedByTheSingletonConnectionSourceResolveFromTheRootProvider()
    {
        var services = new ServiceCollection();
        services.AddExternalAuthenticationServices();

        var feature = CreatePersistenceFeature(services);
        feature.Apply();

        // DatabaseIdentityProviderConnectionSource is registered as a singleton and takes both of these stores, so
        // neither may be scoped. Under ValidateScopes, resolving a scoped service from the root provider throws.
        // ValidateOnBuild is deliberately off: this container holds external authentication only, so unrelated
        // descriptors are legitimately unsatisfiable here.
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await Assert.That(serviceProvider.GetRequiredService<IIdentityProviderConnectionStore>()).IsOfType(typeof(EFCoreIdentityProviderConnectionStore));
        await Assert.That(serviceProvider.GetRequiredService<IConnectionRegistryVersionStore>()).IsOfType(typeof(EFCoreConnectionRegistryVersionStore));
    }

    private static EFCoreExternalAuthenticationPersistenceFeature CreatePersistenceFeature(IServiceCollection services)
    {
        var module = Mock.Of<IModule>().Object;
        Mock.Get(module).Services.Returns(services);
        return new EFCoreExternalAuthenticationPersistenceFeature(module)
            .UsePostgreSql("Host=localhost;Database=elsa;Username=elsa;Password=elsa");
    }
}
