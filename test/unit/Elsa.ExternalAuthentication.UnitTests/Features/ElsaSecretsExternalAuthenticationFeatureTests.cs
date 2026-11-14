using Elsa.ExternalAuthentication.Contracts;
using Elsa.ExternalAuthentication.Secrets.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Elsa.ExternalAuthentication.UnitTests.Features;

public class ElsaSecretsExternalAuthenticationFeatureTests
{
    [Test]
    public async Task ServiceRegistrationAddsTheManagedSecretBridge()
    {
        var services = new ServiceCollection();
        services.AddElsaSecretsExternalAuthentication();

        await Assert.That(services).Contains(descriptor =>
            descriptor.ServiceType == typeof(ISecretBindingResolver) &&
            descriptor.ImplementationType == typeof(ElsaSecretBindingResolver));
        await Assert.That(services).Contains(descriptor =>
            descriptor.ServiceType == typeof(IManagedSecretBindingWriter) &&
            descriptor.ImplementationType == typeof(ElsaSecretBindingResolver));
    }
}
