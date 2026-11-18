using Elsa.Features.Contracts;
using Elsa.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Features.Services;

internal static class FeatureServiceRegistration
{
    internal static void RegisterHostedServices(IServiceCollection services, int insertion, IEnumerable<ServiceDescriptor> descriptors)
    {
        var appended = services.Count;
        foreach (var descriptor in descriptors)
            services.TryAddEnumerable(descriptor);

        // Keep TryAddEnumerable's identity checks against registrations made during Apply,
        // then put its new entries back at the original insertion point.
        var count = services.Count - appended;
        for (var offset = 0; offset < count; offset++)
        {
            var descriptor = services[appended + offset];
            services.RemoveAt(appended + offset);
            services.Insert(insertion + offset, descriptor);
        }
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
