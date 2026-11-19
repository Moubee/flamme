using Flamme.Identity.Contracts;
using Flamme.Identity.Endpoints.Users.Create;
using Flamme.UnitTests.Shared;
using FastEndpoints;

namespace Flamme.Identity.UnitTests.Endpoints;

/// <summary>
/// Pins the cleanup the classes in <see cref="FastEndpointsCollection"/> depend on, in the one order that used to fail:
/// an endpoint host runs and is disposed, and only then is an endpoint created through the factory.
/// </summary>
[NotInParallel(nameof(FastEndpointsCollection))]
public class FastEndpointsResolverTests
{
    [Test]
    public async Task FactoryCreatesEndpointsAfterAnEndpointHostIsDisposed()
    {
        var host = new LogoutEndpointTests();
        await host.InitializeAsync();
        await host.DisposeAsync();

        await Assert.That(Factory.Create<Create>(IUserManager.Mock().Object, IRoleAuthorizationService.Mock().Object)).IsNotNull();
    }
}
