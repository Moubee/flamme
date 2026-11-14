using Elsa.Features.Services;
using Elsa.Identity.Contracts;
using Elsa.Identity.Services;
using Microsoft.Extensions.DependencyInjection;
using ModuleIdentityFeature = Elsa.Identity.Features.IdentityFeature;
using System.Threading.Tasks;

namespace Elsa.Identity.UnitTests.Services;

public class DefaultAccessTokenIssuerRegistrationTests
{
    [Test]
    public async Task ModuleFeatureResolvesThePreferredAccessTokenIssuerConstructor()
    {
        var services = CreateServices();
        var module = Mock.Of<IModule>().Object;
        Mock.Get(module).Services.Returns(services);
        new ModuleIdentityFeature(module).Apply();

        await AssertAccessTokenIssuerResolves(services);
    }

    [Test]
    public async Task ModuleFeatureRegistersUserDeletionCoordinator()
    {
        var services = CreateServices();
        var module = Mock.Of<IModule>().Object;
        Mock.Get(module).Services.Returns(services);
        new ModuleIdentityFeature(module).Apply();
        await AssertUserDeletionCoordinatorResolves(services);
    }

    [Test]
    public async Task ModuleFeatureRegistersInMemorySessionRevocation()
    {
        var services = CreateServices();
        var module = Mock.Of<IModule>().Object;
        Mock.Get(module).Services.Returns(services);
        new ModuleIdentityFeature(module).Apply();

        await AssertInMemorySessionRevocationResolves(services);
    }

    private static ServiceCollection CreateServices()
    {
        return new ServiceCollection();
    }

    private static async Task AssertAccessTokenIssuerResolves(IServiceCollection services)
    {
        services.AddScoped(_ => Mock.Of<IElsaTokenService>().Object);
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>()).IsOfType(typeof(DefaultAccessTokenIssuer));
    }

    // SessionRevoker is checked by registration; resolving it also binds token options.
    private static async Task AssertInMemorySessionRevocationResolves(IServiceCollection services)
    {
        await Assert.That(services).Contains(x => x.ServiceType == typeof(SessionRevoker));
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IRevokedSessionStore>()).IsTypeOf<MemoryRevokedSessionStore>();
    }
    private static async Task AssertUserDeletionCoordinatorResolves(IServiceCollection services)
    {
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IUserDeletionCoordinator>()).IsOfType(typeof(UserDeletionCoordinator));
    }
}
