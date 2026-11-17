using Elsa.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Features.Services;

/// <summary>
/// An immutable producer-owned fragment of typed feature and hosted-service bindings.
/// </summary>
public sealed class FeatureModuleExport : ModuleExport
{
    internal FeatureModuleExport(ModuleExportDescriptor descriptor, IEnumerable<FeatureRegistration> features,
        IEnumerable<KeyValuePair<Type, ServiceDescriptor>> hostedServices) : base(descriptor)
    {
        Features = Array.AsReadOnly(features.ToArray());
        HostedServices = Array.AsReadOnly(hostedServices.ToArray());
        FeatureTypes = Array.AsReadOnly(Features.Select(feature => feature.Type).ToArray());
        HostedServiceTypes = Array.AsReadOnly(HostedServices.Select(service => service.Key).ToArray());
    }

    /// <summary>
    /// Gets the fragment's feature keys in producer declaration order.
    /// </summary>
    public IReadOnlyList<Type> FeatureTypes { get; }

    /// <summary>
    /// Gets the fragment's hosted-service keys in producer declaration order.
    /// </summary>
    public IReadOnlyList<Type> HostedServiceTypes { get; }

    internal IReadOnlyList<FeatureRegistration> Features { get; }
    internal IReadOnlyList<KeyValuePair<Type, ServiceDescriptor>> HostedServices { get; }
}