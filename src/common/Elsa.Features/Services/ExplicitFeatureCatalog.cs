using System.Collections.Frozen;
using System.Collections.Immutable;
using Elsa.Features.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Features.Services;

/// <summary>
/// Immutable feature metadata and factories shared by explicit and generated composition.
/// </summary>
public sealed class ExplicitFeatureCatalog : IFeatureProvider
{
    private readonly FrozenDictionary<Type, FeatureRegistration> _features;
    private readonly FrozenDictionary<Type, ServiceDescriptor> _hostedServices;

    internal ExplicitFeatureCatalog(IDictionary<Type, FeatureRegistration> features, IDictionary<Type, ServiceDescriptor> hostedServices)
    {
        FeatureTypes = features.Keys.ToImmutableArray();
        _features = features.ToFrozenDictionary();
        HostedServiceTypes = hostedServices.Keys.ToImmutableArray();
        _hostedServices = hostedServices.ToFrozenDictionary();
    }

    /// <summary>
    /// Gets the declared feature types in declaration order.
    /// </summary>
    public IReadOnlyList<Type> FeatureTypes { get; }

    /// <summary>
    /// Gets the declared hosted-service implementation types in declaration order.
    /// </summary>
    public IReadOnlyList<Type> HostedServiceTypes { get; }

    IFeature IFeatureProvider.CreateFeature(Type type, IModule module)
    {
        var feature = Get(type).Factory(module);
        if (feature is null || feature.GetType() != type)
            throw new InvalidOperationException($"Factory for feature {type} returned {feature?.GetType().ToString() ?? "null"}; an exact feature identity is required.");
        return feature;
    }

    IReadOnlyList<Type> IFeatureProvider.GetDependencies(Type type) => Get(type).Dependencies;
    IReadOnlyList<Type> IFeatureProvider.GetConditionalDependencies(Type type) => Get(type).DependencyOf;
    FeatureDescriptor IFeatureProvider.GetDescriptor(Type type)
    {
        var entry = Get(type);
        return new(entry.Name, entry.Namespace, entry.DisplayName, entry.Description);
    }

    ServiceDescriptor IFeatureProvider.CreateHostedServiceDescriptor(Type type)
        => _hostedServices.TryGetValue(type, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Missing catalog entry for hosted service {type}.");

    private FeatureRegistration Get(Type type) => _features.TryGetValue(type, out var entry)
        ? entry
        : throw new InvalidOperationException($"Missing catalog entry for feature {type}.");
}