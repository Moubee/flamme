using Elsa.Features.Contracts;
using Elsa.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Features.Services;

internal static class FeatureServiceRegistration
{
    internal static void RegisterHostedServices(IServiceCollection services, ServiceDescriptor[] beforeApply, IEnumerable<ServiceDescriptor> descriptors, bool orderExisting = false)
    {
        var insertion = InsertionPoint(services, beforeApply);
        var ordered = new List<ServiceDescriptor>();
        foreach (var descriptor in descriptors)
        {
            var count = services.Count;
            services.TryAddEnumerable(descriptor);
            if (services.Count != count || orderExisting && services.Contains(descriptor))
                ordered.Add(descriptor);
        }

        // Work back from the boundary. On repeated fixed-module application, an existing
        // descriptor anchors higher-priority requests at its original position.
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var descriptor = ordered[i];
            var index = services.IndexOf(descriptor);
            if (index > insertion)
            {
                services.RemoveAt(index);
                services.Insert(insertion, descriptor);
            }
            else
                insertion = index;
        }
    }

    private static int InsertionPoint(IServiceCollection services, ServiceDescriptor[] beforeApply)
    {
        var originals = new HashSet<ServiceDescriptor>(beforeApply, ReferenceEqualityComparer.Instance);
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var current = services[i];
            if (originals.Contains(current))
                return i + 1;
            // A decorator can replace a descriptor at its original service, key and index.
            // A descriptor that still exists elsewhere anchors its own position.
            if (i < beforeApply.Length && beforeApply[i].ServiceType == current.ServiceType &&
                Equals(beforeApply[i].ServiceKey, current.ServiceKey) && !services.Contains(beforeApply[i]))
                return i + 1;
        }
        return 0;
    }

    internal static void PublishInstalledFeatures(IServiceCollection services, IEnumerable<FeatureDescriptor> descriptors)
    {
        var registry = new InstalledFeatureRegistry();
        foreach (var descriptor in descriptors)
            registry.Add(descriptor);
        services.AddSingleton<IInstalledFeatureRegistry>(registry);
        services.AddSingleton<IInstalledFeatureProvider>(sp => new InstalledFeatureProvider(sp.GetRequiredService<IInstalledFeatureRegistry>()));
    }
}
