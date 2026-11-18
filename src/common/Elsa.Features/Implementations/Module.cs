using Elsa.Extensions;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.Implementations;

/// <inheritdoc />
public class Module : IModule
{
    private sealed record HostedServiceDescriptor(int Order, Type Type);

    private readonly IFeatureProvider _provider;
    private readonly CompositionPolicy _policy;
    private readonly HashSet<IFeature> _configuringFeatures = [];
    private readonly HashSet<Type> _constructingFeatures = [];
    private Exception? _fault;
    private Type? _activeFeature;
    private bool _applyInProgress;
    private bool TracksFailures => _provider is ExplicitFeatureCatalog || _policy == CompositionPolicy.ValidatedV1;
    private Dictionary<Type, IFeature> _features = new();
    private readonly HashSet<IFeature> _configuredFeatures = new();
    private readonly List<HostedServiceDescriptor> _hostedServiceDescriptors = new();

    /// <summary>
    /// Constructor.
    /// </summary>
    public Module(IServiceCollection services) : this(services, new ReflectionFeatureProvider(), CompositionPolicy.LegacyV1)
    {
    }

    /// <summary>
    /// Creates a module using managed discovery and an explicitly selected admission policy.
    /// </summary>
    public Module(IServiceCollection services, CompositionPolicy policy) : this(services, new ReflectionFeatureProvider(), policy)
    {
    }

    /// <summary>
    /// Creates a module using only the supplied catalog, without a reflection fallback.
    /// </summary>
    public Module(IServiceCollection services, ExplicitFeatureCatalog catalog, CompositionPolicy policy = CompositionPolicy.LegacyV1)
        : this(services, (IFeatureProvider)catalog, policy)
    {
    }

    private Module(IServiceCollection services, IFeatureProvider provider, CompositionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(provider);
        if (policy is not CompositionPolicy.LegacyV1 and not CompositionPolicy.ValidatedV1)
            throw new ArgumentOutOfRangeException(nameof(policy));
        Services = services;
        _provider = provider;
        _policy = policy;
    }

    /// <summary>
    /// Gets the catalog selected at creation, or null when this module uses managed discovery.
    /// </summary>
    public ExplicitFeatureCatalog? Catalog => _provider as ExplicitFeatureCatalog;

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();

    /// <inheritdoc />
    public bool HasFeature<T>() where T : class, IFeature
    {
        return HasFeature(typeof(T));
    }

    /// <inheritdoc />
    public bool HasFeature(Type featureType)
    {
        return _features.ContainsKey(featureType);
    }

    /// <inheritdoc />
    public T Configure<T>(Action<T>? configure = null) where T : class, IFeature
    {
        return Configure(module => (T)_provider.CreateFeature(typeof(T), module), configure);
    }

    /// <inheritdoc />
    public T Configure<T>(Func<IModule, T> factory, Action<T>? configure = null) where T : class, IFeature
    {
        EnsureUsable();
        try
        {
            ArgumentNullException.ThrowIfNull(factory);
            var type = typeof(T);
            if (_provider is ExplicitFeatureCatalog)
                RequireDeclaredFeature(type);
            if (!_features.TryGetValue(type, out var feature))
            {
                if (_policy == CompositionPolicy.ValidatedV1)
                    FeatureGraph.Sort([type], GetValidatedDependencies);
                feature = ConstructFeature(type, module => factory(module));
                _features[type] = feature!;
            }

            configure?.Invoke((T)feature);
            EnsureUsable();
            if (!_isApplying)
                return (T)feature;
            if (_policy == CompositionPolicy.ValidatedV1)
            {
                ConfigureValidatedFeatures();
                if (!_applyInProgress)
                    AdmitValidatedFeatures();
            }
            else
            {
                var dependencies = GetDependencyTypes(feature.GetType()).ToHashSet();
                foreach (var dependency in dependencies.Select(GetOrCreateFeature))
                    ConfigureFeature(dependency);
                ConfigureFeature(feature);
            }
            return (T)feature;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <inheritdoc />
    public IModule ConfigureHostedService<T>(int priority = 0) where T : class, IHostedService
    {
        return ConfigureHostedService(typeof(T), priority);
    }

    /// <inheritdoc />
    public IModule ConfigureHostedService(Type hostedServiceType, int priority = 0)
    {
        EnsureUsable();
        try
        {
            if (_provider is ExplicitFeatureCatalog)
            {
                try
                {
                    _provider.CreateHostedServiceDescriptor(hostedServiceType);
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException($"Feature {_activeFeature?.ToString() ?? "<module>"} requested hosted service {hostedServiceType}: {exception.Message}", exception);
                }
            }
            _hostedServiceDescriptors.Add(new(priority, hostedServiceType));
            return this;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    private bool _isApplying;

    /// <inheritdoc />
    public void Apply()
    {
        EnsureUsable();
        try
        {
            if (TracksFailures && _applyInProgress)
                throw new InvalidOperationException("Feature application cannot recursively apply the same module.");
            _applyInProgress = true;
            if (_policy == CompositionPolicy.ValidatedV1)
                ApplyValidated();
            else
                ApplyLegacy();
            EnsureUsable();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            _applyInProgress = false;
        }
    }

    private void ApplyLegacy()
    {
        _isApplying = true;
        var featureTypes = GetFeatureTypes();
        _features = featureTypes.ToDictionary(featureType => featureType, featureType => _features.TryGetValue(featureType, out var existingFeature) ? existingFeature : ConstructFeature(featureType, module => _provider.CreateFeature(featureType, module)));

        // Iterate over a copy of the features to avoid concurrent modification exceptions.
        foreach (var feature in _features.Values.ToList())
        {
            // This will cause additional features to be added to _features.
            ConfigureFeature(feature);
        }

        // Filter out features that depend on other features that are not installed.
        _features = ExcludeFeaturesWithMissingDependencies(_features.Values).ToDictionary(x => x.GetType(), x => x);

        // Hosted services are registered after the features have been applied, because applying a feature can contribute more of them. They still belong
        // at this position in the service collection though, ahead of whatever the features register from their Apply method, so remember where that is.
        var beforeApply = Services.ToArray();

        // Make sure to use the complete list of features when applying them. Applying a feature can introduce additional features (a feature calling
        // Module.Configure<T>() from its Apply method, directly or through a helper such as AddActivity<T>()), so keep going until nothing new shows up.
        var appliedFeatures = new HashSet<IFeature>();
        while (GetFeaturesPendingApply(appliedFeatures) is { Count: > 0 } pendingFeatures)
        {
            appliedFeatures.UnionWith(pendingFeatures);

            foreach (var feature in pendingFeatures)
                InvokeApply(feature);
        }

        // Add hosted services in order of priority.
        RegisterHostedServices(beforeApply);

        PublishInstalledFeatures();
    }

    private IEnumerable<IFeature> ExcludeFeaturesWithMissingDependencies(IEnumerable<IFeature> features)
    {
        return
            from feature in features
            let featureType = feature.GetType()
            let conditionalDependencies = _provider.GetConditionalDependencies(featureType)
            let missingDependencies = conditionalDependencies.Where(x => !_features.ContainsKey(x)).ToList()
            where missingDependencies.Count == 0
            select feature;
    }

    private void RegisterHostedServices(ServiceDescriptor[] beforeApply) =>
        FeatureServiceRegistration.RegisterHostedServices(Services, beforeApply,
            _hostedServiceDescriptors.OrderBy(entry => entry.Order).Select(entry => _provider.CreateHostedServiceDescriptor(entry.Type)));

    // Returns the features that have not been applied yet, sorted so that dependencies are applied before the features that depend on them.
    private List<IFeature> GetFeaturesPendingApply(IReadOnlySet<IFeature> appliedFeatures)
    {
        var pendingFeatureTypes = _features.Where(x => !appliedFeatures.Contains(x.Value)).Select(x => x.Key).ToList();
        var pendingFeatureTypeLookup = pendingFeatureTypes.ToHashSet();

        // Sorting pulls in dependencies that are not pending themselves, so filter those out again.
        return pendingFeatureTypes
            .TSort(_provider.GetDependencies)
            .Where(pendingFeatureTypeLookup.Contains)
            .Select(x => _features[x])
            .Distinct()
            .ToList();
    }

    private void ConfigureFeature(IFeature feature)
    {
        if (_configuredFeatures.Contains(feature) || TracksFailures && _configuringFeatures.Contains(feature))
            return;
        _configuringFeatures.Add(feature);
        var previous = _activeFeature;
        try
        {
            _activeFeature = feature.GetType();
            feature.Configure();
            EnsureUsable();
            feature.ConfigureHostedServices();
            EnsureUsable();
            _features[feature.GetType()] = feature;
            _configuredFeatures.Add(feature);
        }
        finally
        {
            _activeFeature = previous;
            _configuringFeatures.Remove(feature);
        }
    }

    private IFeature GetOrCreateFeature(Type featureType)
    {
        return _features.TryGetValue(featureType, out var existingFeature) ? existingFeature : ConstructFeature(featureType, module => _provider.CreateFeature(featureType, module));
    }

    private HashSet<Type> GetFeatureTypes()
    {
        var featureTypes = _features.Keys.ToHashSet();
        var featureTypesWithDependencies = featureTypes.Concat(featureTypes.SelectMany(GetDependencyTypes)).ToHashSet();
        return featureTypesWithDependencies.TSort(_provider.GetDependencies).ToHashSet();
    }

    // Recursively get dependency types.
    private IEnumerable<Type> GetDependencyTypes(Type type)
    {
        var dependencies = _provider.GetDependencies(type).ToList();
        return dependencies.Concat(dependencies.SelectMany(GetDependencyTypes));
    }

    private IFeature ConstructFeature(Type type, Func<IModule, IFeature> factory)
    {
        EnsureUsable();
        if (TracksFailures && !_constructingFeatures.Add(type))
            throw new InvalidOperationException($"Feature factory recursively requested {type} before construction completed.");
        var previous = _activeFeature;
        try
        {
            _activeFeature = type;
            var feature = factory(this);
            EnsureUsable();
            if (_provider is ExplicitFeatureCatalog && (feature is null || feature.GetType() != type))
                throw new InvalidOperationException($"Factory for feature {type} returned {feature?.GetType().ToString() ?? "null"}; an exact feature identity is required.");
            return feature!;
        }
        finally
        {
            _activeFeature = previous;
            _constructingFeatures.Remove(type);
        }
    }

    private void EnsureUsable()
    {
        if (_fault is not null)
            throw new InvalidOperationException("The module is faulted; rebuild composition with a fresh service collection.", _fault);
    }

    private void RecordFailure(Exception exception)
    {
        if (TracksFailures)
            _fault ??= exception;
    }

    private void RequireDeclaredFeature(Type type)
    {
        try
        {
            _provider.GetDescriptor(type);
        }
        catch (InvalidOperationException exception)
        {
            var path = _activeFeature is null ? [type] : new[] { _activeFeature, type };
            throw new InvalidOperationException($"Feature admission {FeatureGraph.Format(path)}: {exception.Message}", exception);
        }
    }

    private IReadOnlyList<Type> GetValidatedDependencies(Type type)
    {
        if (type.ContainsGenericParameters || !type.IsClass || type.IsAbstract || !typeof(IFeature).IsAssignableFrom(type))
            throw new InvalidOperationException($"Feature dependency {type} must be a closed, concrete IFeature type.");
        return _provider.GetDependencies(type);
    }

    private void ConfigureValidatedFeatures()
    {
        while (true)
        {
            var ordered = FeatureGraph.Sort(_features.Keys, GetValidatedDependencies);
            foreach (var type in ordered)
            {
                if (!_features.ContainsKey(type))
                    _features.Add(type, ConstructFeature(type, module => _provider.CreateFeature(type, module)));
            }
            var pending = ordered.Select(type => _features[type])
                .Where(feature => !_configuredFeatures.Contains(feature) && !_configuringFeatures.Contains(feature)).ToList();
            if (pending.Count == 0)
                return;
            foreach (var feature in pending)
                ConfigureFeature(feature);
        }
    }

    private void ApplyValidated()
    {
        _isApplying = true;
        ConfigureValidatedFeatures();
        AdmitValidatedFeatures();
        var beforeApply = Services.ToArray();
        var applied = new HashSet<IFeature>();
        while (true)
        {
            ConfigureValidatedFeatures();
            AdmitValidatedFeatures(applied);
            var next = FeatureGraph.Sort(_features.Keys, GetValidatedDependencies)
                .Select(type => _features[type]).FirstOrDefault(feature => !applied.Contains(feature));
            if (next is null)
                break;
            applied.Add(next);
            InvokeApply(next);
        }
        RegisterHostedServices(beforeApply);
        PublishInstalledFeatures();
    }

    private void AdmitValidatedFeatures(IReadOnlySet<IFeature>? applied = null)
    {
        var admitted = _features.Keys.ToHashSet();
        var exclusions = new Dictionary<Type, Type>();
        var validated = new HashSet<Type>();
        bool changed;
        do
        {
            changed = false;
            foreach (var type in admitted.ToArray())
            {
                var missing = _provider.GetConditionalDependencies(type).FirstOrDefault(target => !admitted.Contains(target));
                if (missing is null)
                    continue;
                admitted.Remove(type);
                exclusions[type] = missing;
                changed = true;
            }
        } while (changed);

        foreach (var type in _features.Keys)
        {
            if (!admitted.Contains(type))
            {
                if (applied?.Contains(_features[type]) == true)
                    throw new InvalidOperationException($"Already-applied feature {type} lost its conditional admission.");
                continue;
            }
            ValidateStrongPath(type, []);
        }
        _features = _features.Where(entry => admitted.Contains(entry.Key)).ToDictionary();
        return;

        void ValidateStrongPath(Type type, List<Type> path)
        {
            if (!validated.Add(type))
                return;
            path.Add(type);
            foreach (var dependency in _provider.GetDependencies(type))
            {
                if (!admitted.Contains(dependency))
                {
                    var conditionalPath = new List<Type> { dependency };
                    var current = dependency;
                    while (exclusions.TryGetValue(current, out var reason))
                    {
                        conditionalPath.Add(reason);
                        current = reason;
                    }
                    throw new InvalidOperationException($"Feature dependency {FeatureGraph.Format(path.Append(dependency))} was excluded by conditional dependency {FeatureGraph.Format(conditionalPath)}.");
                }
                ValidateStrongPath(dependency, path);
            }
            path.RemoveAt(path.Count - 1);
        }
    }

    private void InvokeApply(IFeature feature)
    {
        var previous = _activeFeature;
        try
        {
            _activeFeature = feature.GetType();
            feature.Apply();
            EnsureUsable();
        }
        finally
        {
            _activeFeature = previous;
        }
    }

    private void PublishInstalledFeatures() =>
        FeatureServiceRegistration.PublishInstalledFeatures(Services, _features.Values.Select(feature => _provider.GetDescriptor(feature.GetType())));
}
