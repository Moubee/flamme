using System.Diagnostics.CodeAnalysis;
using Elsa.CodeGeneration;
using Elsa.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.Services;

/// <summary>
/// Declares closed feature factories and their metadata without runtime member discovery.
/// </summary>
public sealed class ExplicitFeatureCatalogBuilder
{
    private readonly List<FeatureRegistration> _features = [];
    private readonly List<KeyValuePair<Type, ServiceDescriptor>> _hostedServices = [];

    /// <summary>
    /// Adds a typed factory and snapshots its metadata and dependency keys.
    /// </summary>
    public ExplicitFeatureCatalogBuilder Add<TFeature>(Func<IModule, TFeature> factory, FeatureDescriptor metadata,
        IEnumerable<Type>? dependencies = null, IEnumerable<Type>? dependencyOf = null) where TFeature : class, IFeature
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Add(typeof(TFeature), module => factory(module), metadata, dependencies, dependencyOf);
    }

    /// <summary>
    /// Adds an explicitly constructed feature identified by a closed runtime type.
    /// </summary>
    public ExplicitFeatureCatalogBuilder Add(Type featureType, Func<IModule, IFeature> factory, FeatureDescriptor metadata,
        IEnumerable<Type>? dependencies = null, IEnumerable<Type>? dependencyOf = null)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateType(featureType);
        if (string.IsNullOrWhiteSpace(metadata.Name) || string.IsNullOrWhiteSpace(metadata.Namespace) || string.IsNullOrWhiteSpace(metadata.DisplayName))
            throw new InvalidOperationException($"Feature {featureType} requires a name, namespace, and display name.");
        var strong = dependencies?.ToArray() ?? [];
        var conditional = dependencyOf?.ToArray() ?? [];
        foreach (var type in strong.Concat(conditional))
            ValidateType(type);
        _features.Add(new(featureType, factory, metadata.Name, metadata.Namespace, metadata.DisplayName, metadata.Description,
            Array.AsReadOnly(strong), Array.AsReadOnly(conditional)));
        return this;
    }

    /// <summary>
    /// Declares a closed hosted-service implementation, retaining its public constructors for DI.
    /// </summary>
    public ExplicitFeatureCatalogBuilder AddHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>()
        where TService : class, IHostedService
    {
        var type = typeof(TService);
        if (type.ContainsGenericParameters || !type.IsClass || type.IsAbstract)
            throw new InvalidOperationException($"Hosted service {type} must be a closed, concrete IHostedService type.");
        _hostedServices.Add(new(type, ServiceDescriptor.Singleton<IHostedService, TService>()));
        return this;
    }

    /// <summary>
    /// Freezes the catalog and rejects duplicate identities, missing dependencies, and cycles.
    /// </summary>
    public ExplicitFeatureCatalog Build()
    {
        var entries = CollectFeatures();
        FeatureGraph.Sort(entries.Keys, type => entries.TryGetValue(type, out var entry)
            ? entry.Dependencies
            : throw new InvalidOperationException($"Missing catalog entry for feature {type}."));
        return new ExplicitFeatureCatalog(entries, CollectHostedServices());
    }

    /// <summary>
    /// Freezes a producer's bindings without requiring its external feature dependencies locally.
    /// The final module catalog validates the complete dependency graph after imports are selected.
    /// </summary>
    public FeatureModuleExport CreateExport(ModuleExportDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        try
        {
            var entries = CollectFeatures();
            // Local cycles are already invalid; external edges are validated after composition.
            FeatureGraph.Sort(entries.Keys, type => entries[type].Dependencies.Where(entries.ContainsKey).ToArray());
            return new FeatureModuleExport(descriptor, entries.Values, CollectHostedServices());
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Module {descriptor.ModuleId} contract {descriptor.ContractVersion} has invalid feature bindings: {exception.Message}", exception);
        }
    }

    private Dictionary<Type, FeatureRegistration> CollectFeatures()
    {
        var entries = new Dictionary<Type, FeatureRegistration>();
        var identities = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var entry in _features)
        {
            if (!entries.TryAdd(entry.Type, entry))
                throw new InvalidOperationException($"Duplicate feature type {entry.Type}.");
            var identity = $"{entry.Namespace}.{entry.Name}";
            if (!identities.TryAdd(identity, entry.Type))
                throw new InvalidOperationException($"Duplicate feature identity {identity}: {identities[identity]} and {entry.Type}.");
        }
        return entries;
    }

    private Dictionary<Type, ServiceDescriptor> CollectHostedServices()
    {
        var hostedServices = new Dictionary<Type, ServiceDescriptor>();
        foreach (var entry in _hostedServices)
        {
            if (!hostedServices.TryAdd(entry.Key, entry.Value))
                throw new InvalidOperationException($"Duplicate hosted-service type {entry.Key}.");
        }
        return hostedServices;
    }

    private static void ValidateType(Type type)
    {
        if (type is null || type.ContainsGenericParameters || !type.IsClass || type.IsAbstract || !typeof(IFeature).IsAssignableFrom(type))
            throw new InvalidOperationException($"Feature key {type} must be a closed, concrete IFeature type.");
    }
}

internal sealed record FeatureRegistration(Type Type, Func<IModule, IFeature> Factory, string Name, string Namespace,
    string DisplayName, string Description, IReadOnlyList<Type> Dependencies, IReadOnlyList<Type> DependencyOf);