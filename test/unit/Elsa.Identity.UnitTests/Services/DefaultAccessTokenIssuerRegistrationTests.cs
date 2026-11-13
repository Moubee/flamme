using Elsa.Features.Services;
using Elsa.Identity.Contracts;
using Elsa.Identity.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ModuleIdentityFeature = Elsa.Identity.Features.IdentityFeature;
using ShellIdentityFeature = Elsa.Identity.ShellFeatures.IdentityFeature;
using System.Threading.Tasks;

namespace Elsa.Identity.UnitTests.Services;

public class DefaultAccessTokenIssuerRegistrationTests
{
    [Test]
    public async Task ModuleFeatureResolvesThePreferredAccessTokenIssuerConstructor()
    {
        var services = CreateServices();
        var module = Substitute.For<IModule>();
        module.Services.Returns(services);
        new ModuleIdentityFeature(module).Apply();

        await AssertAccessTokenIssuerResolves(services);
    }

    [Test]
    public async Task ShellFeatureResolvesThePreferredAccessTokenIssuerConstructor()
    {
        var services = CreateServices();
        new ShellIdentityFeature().ConfigureServices(services);

        await AssertAccessTokenIssuerResolves(services);
    }

    [Test]
    public async Task ModuleFeatureRegistersUserDeletionCoordinator()
    {
        var services = CreateServices();
        var module = Substitute.For<IModule>();
        module.Services.Returns(services);
        new ModuleIdentityFeature(module).Apply();
        await AssertUserDeletionCoordinatorResolves(services);
    }

    [Test]
    public async Task ShellFeatureRegistersUserDeletionCoordinator()
    {
        var services = CreateServices();
        new ShellIdentityFeature().ConfigureServices(services);
        await AssertUserDeletionCoordinatorResolves(services);
    }

    [Test]
    public async Task ModuleFeatureRegistersInMemorySessionRevocation()
    {
        var services = CreateServices();
        var module = Substitute.For<IModule>();
        module.Services.Returns(services);
        new ModuleIdentityFeature(module).Apply();

        await AssertInMemorySessionRevocationResolves(services);
    }

    [Test]
    public async Task ShellFeatureRegistersInMemorySessionRevocation()
    {
        var services = CreateServices();
        new ShellIdentityFeature().ConfigureServices(services);

        await AssertInMemorySessionRevocationResolves(services);
    }

    private static ServiceCollection CreateServices()
    {
        return new ServiceCollection();
    }

    private static async Task AssertAccessTokenIssuerResolves(IServiceCollection services)
    {
        services.AddScoped(_ => Substitute.For<IElsaTokenService>());
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>()).IsOfType(typeof(DefaultAccessTokenIssuer));
    }

    // SessionRevoker is checked by registration: resolving it binds the token options, which the shell feature reads from shell configuration.
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