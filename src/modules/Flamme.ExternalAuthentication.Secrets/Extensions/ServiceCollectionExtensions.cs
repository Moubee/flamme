using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Secrets.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the optional Flamme Secrets bridge for External Authentication.
    /// </summary>
    public static IServiceCollection AddFlammeSecretsExternalAuthentication(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretBindingResolver, FlammeSecretBindingResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IManagedSecretBindingWriter, FlammeSecretBindingResolver>());
        return services;
    }
}
