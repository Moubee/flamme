using Elsa.Features.Models;

namespace Elsa.Features.Services;

/// <summary>A host-local slot for a selected feature, including features internal to their producer.</summary>
public sealed class FixedFeatureSlot
{
    private readonly FixedFeatureModule _module;
    private readonly Type _type;
    private readonly Func<IModule, IFeature>? _factory;
    private readonly FeatureDescriptor _metadata;
    private IFeature? _instance;
    private bool _constructing;

    internal FixedFeatureSlot(FixedFeatureModule module, Type type, Func<IModule, IFeature>? factory, FeatureDescriptor metadata)
    {
        _module = module;
        _type = type;
        _factory = factory;
        _metadata = new(metadata.Name, metadata.Namespace, metadata.DisplayName, metadata.Description);
    }

    internal FeatureDescriptor GetDescriptor() =>
        new(_metadata.Name, _metadata.Namespace, _metadata.DisplayName, _metadata.Description);

    internal IFeature Resolve(Func<IModule, IFeature>? factory = null)
    {
        if (_instance is not null)
            return _instance;
        if (_constructing)
            throw new InvalidOperationException($"Factory for feature {_type} recursively requested its own instance.");
        var create = factory ?? _factory ?? throw new MissingMethodException($"Feature {_type} has no default factory. Supply a factory with Configure<T>(factory, configure) before applying the fixed plan.");
        _constructing = true;
        try
        {
            var feature = create(_module);
            _module.EnsureUsable();
            if (feature is null || feature.GetType() != _type)
                throw new InvalidOperationException($"Factory for feature {_type} must return that exact feature type.");
            return _instance = feature;
        }
        finally
        {
            _constructing = false;
        }
    }

    /// <summary>Runs this feature's Configure and ConfigureHostedServices callbacks.</summary>
    public void Configure()
    {
        _module.EnsureUsable();
        var feature = Resolve();
        feature.Configure();
        _module.EnsureUsable();
        feature.ConfigureHostedServices();
        _module.EnsureUsable();
    }

    /// <summary>Runs this feature's Apply callback.</summary>
    public void Apply()
    {
        _module.EnsureUsable();
        Resolve().Apply();
        _module.EnsureUsable();
    }
}
