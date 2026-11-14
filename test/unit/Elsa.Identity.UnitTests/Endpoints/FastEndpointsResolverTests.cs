using Elsa.Identity.Contracts;
using Elsa.Identity.Endpoints.Users.Create;
using Elsa.UnitTests.Shared;
using FastEndpoints;

namespace Elsa.Identity.UnitTests.Endpoints;

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

        await Assert.That(Factory.Create<Create>(Mock.Of<IUserManager>().Object, Mock.Of<IRoleAuthorizationService>().Object)).IsNotNull();
    }
}
