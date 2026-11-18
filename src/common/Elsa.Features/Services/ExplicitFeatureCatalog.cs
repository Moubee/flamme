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

    internal ExplicitFeatureCatalog(IDictionary<Type, FeatureRegistration> features, IDictionary<Type, ServiceDescriptor> hostedServices, bool compiledDeclarations = false)
    {
        CompiledDeclarations = compiledDeclarations;
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

    internal bool CompiledDeclarations { get; }

    internal IReadOnlyList<Type> AvailableDependencies(Type type) =>
        _features.TryGetValue(type, out var feature) ? feature.Dependencies : [];

    IFeature IFeatureProvider.CreateFeature(Type type, IModule module)
    {
        var factory = Get(type).Factory ?? throw new MissingFeatureConstructorException(type);
        IFeature feature;
        try
        {
            feature = factory(module);
        }
        catch (Exception exception) when (CompiledDeclarations)
        {
            // Activator.CreateInstance wraps exceptions from the selected constructor.
            throw new System.Reflection.TargetInvocationException(exception);
        }
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

internal sealed class MissingFeatureConstructorException(Type featureType) : MissingMethodException(
    $"Feature {featureType} has no supported public module constructor. Supply a factory with Configure<T>(factory, configure).");
