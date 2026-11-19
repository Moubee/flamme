using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Secrets.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Flamme.ExternalAuthentication.UnitTests.Features;

public class FlammeSecretsExternalAuthenticationFeatureTests
{
    [Test]
    public async Task ServiceRegistrationAddsTheManagedSecretBridge()
    {
        var services = new ServiceCollection();
        services.AddFlammeSecretsExternalAuthentication();

        await Assert.That(services).Contains(descriptor =>
            descriptor.ServiceType == typeof(ISecretBindingResolver) &&
            descriptor.ImplementationType == typeof(FlammeSecretBindingResolver));
        await Assert.That(services).Contains(descriptor =>
            descriptor.ServiceType == typeof(IManagedSecretBindingWriter) &&
            descriptor.ImplementationType == typeof(FlammeSecretBindingResolver));
    }
}
