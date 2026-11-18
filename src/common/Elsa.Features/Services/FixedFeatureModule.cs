using System.Diagnostics.CodeAnalysis;
using Elsa.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.Services;

/// <summary>
/// Executes the feature slots and lifecycle order compiled into a host. Each module owns its feature instances.
/// </summary>
public abstract class FixedFeatureModule : IModule
{
    private readonly Dictionary<Type, FixedFeatureSlot> _slots = [];
    private readonly Dictionary<Type, ServiceDescriptor> _hostedServices = [];
    private readonly List<(int Priority, ServiceDescriptor Descriptor)> _requestedServices = [];
    private Action<IModule>? _configureHost;
    private Exception? _fault;
    private bool _sealed;
    private bool _started;
    private bool _applying;

    /// <summary>Creates the per-host state used by a generated plan.</summary>
    protected FixedFeatureModule(IServiceCollection services) => Services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();

    /// <summary>Declares one selected feature from its producer's generated constructor and metadata.</summary>
    public FixedFeatureSlot Declare<TFeature>(Func<IModule, TFeature>? factory, FeatureDescriptor metadata) where TFeature : class, IFeature
    {
        EnsureDeclaring();
        ArgumentNullException.ThrowIfNull(metadata);
        var slot = new FixedFeatureSlot(this, typeof(TFeature), factory, metadata);
        _slots.Add(typeof(TFeature), slot);
        return slot;
    }

    /// <summary>Declares a hosted-service binding used by the selected features.</summary>
    public void DeclareHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>()
        where TService : class, IHostedService
    {
        EnsureDeclaring();
        _hostedServices.TryAdd(typeof(TService), ServiceDescriptor.Singleton<IHostedService, TService>());
    }

    /// <summary>Closes declarations before the generated module is returned to the host.</summary>
    protected void SealDeclarations() => _sealed = true;

    private void EnsureDeclaring()
    {
        if (_sealed)
            throw new InvalidOperationException("The fixed feature plan is sealed. Declare features and hosted services at build time.");
    }

    private void EnsureSealed()
    {
        if (!_sealed)
            throw new InvalidOperationException("The generated feature plan must seal its declarations before use.");
    }

    internal void EnsureUsable()
    {
        EnsureSealed();
        if (_fault is not null)
            throw new InvalidOperationException("The fixed feature module is faulted. Rebuild with a fresh service collection.", _fault);
    }

    /// <summary>Queues application configuration in registration order for the first call to Apply.</summary>
    public void ConfigureHost(Action<IModule>? configure)
    {
        EnsureUsable();
        if (!_started)
            _configureHost += configure;
    }

    /// <inheritdoc />
    public bool HasFeature<T>() where T : class, IFeature => HasFeature(typeof(T));

    /// <summary>Tests membership in the sealed plan, including features whose constructor has not yet run.</summary>
    public bool HasFeature(Type featureType)
    {
        EnsureSealed();
        return _slots.ContainsKey(featureType);
    }

    /// <inheritdoc />
    public T Configure<T>(Action<T>? configure = null) where T : class, IFeature => Resolve(null, configure);

    /// <inheritdoc />
    public T Configure<T>(Func<IModule, T> factory, Action<T>? configure = null) where T : class, IFeature
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Resolve(factory, configure);
    }

    private T Resolve<T>(Func<IModule, T>? factory, Action<T>? configure) where T : class, IFeature
    {
        EnsureUsable();
        try
        {
            if (!_slots.TryGetValue(typeof(T), out var slot))
                throw new InvalidOperationException($"Feature {typeof(T)} is absent from the fixed feature plan. Declare it at build time.");
            var feature = (T)slot.Resolve(factory);
            configure?.Invoke(feature);
            EnsureUsable();
            return feature;
        }
        catch (Exception exception)
        {
            _fault ??= exception;
            throw;
        }
    }

    /// <inheritdoc />
    public IModule ConfigureHostedService<T>(int priority = 0) where T : class, IHostedService =>
        ConfigureHostedService(typeof(T), priority);

    /// <inheritdoc />
    public IModule ConfigureHostedService(Type hostedServiceType, int priority = 0)
    {
        EnsureUsable();
        if (!_hostedServices.TryGetValue(hostedServiceType, out var descriptor))
        {
            var failure = new InvalidOperationException($"Hosted service {hostedServiceType} is absent from the fixed feature plan.");
            _fault = failure;
            throw failure;
        }
        _requestedServices.Add((priority, descriptor));
        return this;
    }

    /// <summary>Runs all selected features in the generated order. Configuration runs once per module.</summary>
    public void Apply()
    {
        EnsureUsable();
        if (_applying)
        {
            var failure = new InvalidOperationException("Feature application cannot recursively apply the same module.");
            _fault = failure;
            throw failure;
        }
        _applying = true;
        try
        {
            if (!_started)
            {
                _started = true;
                var configureHost = _configureHost;
                _configureHost = null;
                configureHost?.Invoke(this);
                EnsureUsable();
                ConfigureFeatures();
            }
            var beforeApply = Services.ToArray();
            ApplyFeatures();
            EnsureUsable();
            var requested = _requestedServices.OrderBy(entry => entry.Priority).Select(entry => entry.Descriptor).Distinct();
            FeatureServiceRegistration.RegisterHostedServices(Services, beforeApply, requested, orderExisting: true);
            FeatureServiceRegistration.PublishInstalledFeatures(Services, _slots.Values.Select(slot => slot.GetDescriptor()));
        }
        catch (Exception exception)
        {
            _fault ??= exception;
            throw;
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Runs the feature and hosted-service configuration callbacks in generated order.</summary>
    protected abstract void ConfigureFeatures();

    /// <summary>Runs the selected feature application callbacks in generated order.</summary>
    protected abstract void ApplyFeatures();
}
