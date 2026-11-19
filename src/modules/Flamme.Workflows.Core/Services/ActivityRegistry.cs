using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Flamme.Common.Multitenancy;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Logging;

namespace Flamme.Workflows;

/// <inheritdoc />
public class ActivityRegistry(IActivityDescriber activityDescriber, IEnumerable<IActivityDescriptorModifier> modifiers, ITenantAccessor tenantAccessor, ILogger<ActivityRegistry> logger) : IActivityRegistry
{
    private readonly Lock _registrationLock = new();

    // Legacy support for manually registered activities
    private readonly ISet<ActivityDescriptor> _manualActivityDescriptors = new HashSet<ActivityDescriptor>();

    // Per-tenant activity descriptors (workflow-as-activities, tenant-specific providers, etc.)
    private readonly ConcurrentDictionary<string, TenantRegistryData> _tenantRegistries = new();

    // Tenant-agnostic activity descriptors (built-in activities, manually registered, etc.)
    private readonly TenantRegistryData _agnosticRegistry = new();

    // Tracks tenant-agnostic providers initialized for this registry instance.
    private readonly ConcurrentDictionary<Type, byte> _initializedProviders = new();
    private readonly ConcurrentDictionary<Type, SemaphoreSlim> _providerInitializationLocks = new();

    /// <inheritdoc />
    public void Add(Type providerType, ActivityDescriptor descriptor)
    {
        lock (_registrationLock)
        {
            ApplyModifiers(descriptor);
            var registry = GetOrCreateRegistry(descriptor.TenantId);
            var owned = GetOrCreateProviderDescriptors(registry, providerType);
            ValidateBinding(registry, owned, descriptor);
            StoreDescriptor(registry, owned, descriptor);
        }
    }

    /// <inheritdoc />
    public void Remove(Type providerType, ActivityDescriptor descriptor)
    {
        lock (_registrationLock)
        {
            var registry = GetOrCreateRegistry(descriptor.TenantId);
            if (registry.ProvidedActivityDescriptors.TryGetValue(providerType, out var owned) && owned.Remove(descriptor))
                RemoveDescriptor(registry, descriptor);
        }
    }

    /// <inheritdoc />
    public IEnumerable<ActivityDescriptor> ListAll()
    {
        var currentTenantId = tenantAccessor.TenantId;

        // Get descriptors from current tenant's registry
        var tenantDescriptors = _tenantRegistries.TryGetValue(currentTenantId, out var tenantRegistry)
            ? tenantRegistry.ActivityDescriptors.Values
            : Enumerable.Empty<ActivityDescriptor>();

        // Get descriptors from agnostic registry
        var agnosticDescriptors = _agnosticRegistry.ActivityDescriptors.Values;

        return tenantDescriptors.Concat(agnosticDescriptors);
    }

    /// <inheritdoc />
    public IEnumerable<ActivityDescriptor> ListByProvider(Type providerType)
    {
        lock (_registrationLock)
        {
            var currentTenantId = tenantAccessor.TenantId;

            // Get descriptors from current tenant's registry
            var tenantDescriptors = _tenantRegistries.TryGetValue(currentTenantId, out var tenantRegistry) &&
                                    tenantRegistry.ProvidedActivityDescriptors.TryGetValue(providerType, out var tenantProviderDescriptors)
                ? tenantProviderDescriptors
                : Enumerable.Empty<ActivityDescriptor>();

            // Get descriptors from agnostic registry
            var agnosticDescriptors = _agnosticRegistry.ProvidedActivityDescriptors.TryGetValue(providerType, out var agnosticProviderDescriptors)
                ? agnosticProviderDescriptors
                : Enumerable.Empty<ActivityDescriptor>();

            return tenantDescriptors.Concat(agnosticDescriptors).ToArray();
        }
    }

    /// <inheritdoc />
    public ActivityDescriptor? Find(string type)
    {
        var currentTenantId = tenantAccessor.TenantId;

        // Always prefer tenant-specific descriptors over tenant-agnostic ones
        // Get highest version from current tenant's registry
        if (_tenantRegistries.TryGetValue(currentTenantId, out var tenantRegistry))
        {
            if (tenantRegistry.LatestActivityDescriptors.TryGetValue(type, out var tenantDescriptor))
                return tenantDescriptor;
        }

        // Fall back to agnostic registry only if no tenant-specific descriptor exists
        return _agnosticRegistry.LatestActivityDescriptors.TryGetValue(type, out var agnosticDescriptor)
            ? agnosticDescriptor
            : null;
    }

    /// <inheritdoc />
    public ActivityDescriptor? Find(string type, int version)
    {
        var currentTenantId = tenantAccessor.TenantId;

        // Check current tenant's registry first
        if (_tenantRegistries.TryGetValue(currentTenantId, out var tenantRegistry) &&
            tenantRegistry.ActivityDescriptors.TryGetValue((type, version), out var tenantDescriptor))
        {
            return tenantDescriptor;
        }

        // Fall back to agnostic registry
        return _agnosticRegistry.ActivityDescriptors.TryGetValue((type, version), out var agnosticDescriptor)
            ? agnosticDescriptor
            : null;
    }

    /// <inheritdoc />
    public ActivityDescriptor? Find(Func<ActivityDescriptor, bool> predicate)
    {
        var currentTenantId = tenantAccessor.TenantId;

        // Check current tenant's registry first
        if (_tenantRegistries.TryGetValue(currentTenantId, out var tenantRegistry))
        {
            var tenantMatch = tenantRegistry.ActivityDescriptors.Values.FirstOrDefault(predicate);
            if (tenantMatch != null) return tenantMatch;
        }

        // Fall back to agnostic registry
        return _agnosticRegistry.ActivityDescriptors.Values.FirstOrDefault(predicate);
    }

    /// <inheritdoc />
    public IEnumerable<ActivityDescriptor> FindMany(Func<ActivityDescriptor, bool> predicate)
    {
        var currentTenantId = tenantAccessor.TenantId;

        // Get descriptors from current tenant's registry
        var tenantDescriptors = _tenantRegistries.TryGetValue(currentTenantId, out var tenantRegistry)
            ? tenantRegistry.ActivityDescriptors.Values.Where(predicate)
            : Enumerable.Empty<ActivityDescriptor>();

        // Get descriptors from agnostic registry
        var agnosticDescriptors = _agnosticRegistry.ActivityDescriptors.Values.Where(predicate);

        return tenantDescriptors.Concat(agnosticDescriptors);
    }

    /// <inheritdoc />
    public void Register(ActivityDescriptor descriptor) => Add(GetType(), descriptor);

    /// <inheritdoc />
    public async Task RegisterAsync([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType, CancellationToken cancellationToken)
    {
        var name = ActivityTypeNameHelper.GenerateTypeName(activityType);
        var version = ActivityTypeNameHelper.GetVersion(activityType);
        if (Find(name, version)?.ClrType == activityType)
            return;

        var descriptor = await activityDescriber.DescribeActivityAsync(activityType, cancellationToken);
        lock (_registrationLock)
        {
            var existing = Find(descriptor.TypeName, descriptor.Version);
            if (existing != null)
            {
                if (existing.ClrType != activityType)
                    throw ConflictingBinding(descriptor);
                return;
            }
            ApplyModifiers(descriptor);
            var registry = GetOrCreateRegistry(descriptor.TenantId);
            ValidateBinding(registry, _manualActivityDescriptors, descriptor);
            StoreDescriptor(registry, _manualActivityDescriptors, descriptor);
        }
    }

    /// <inheritdoc />
    public async Task RegisterAsync(IEnumerable<Type> activityTypes, CancellationToken cancellationToken = default)
    {
        foreach (var activityType in activityTypes)
            await RegisterAsync(activityType, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<ActivityDescriptor>> GetDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        lock (_registrationLock)
            return new(_manualActivityDescriptors.ToArray());
    }

    /// <inheritdoc />
    public async Task RefreshDescriptorsAsync(IEnumerable<IActivityProvider> activityProviders, CancellationToken cancellationToken = default)
    {
        foreach (var activityProvider in activityProviders)
            await RefreshDescriptorsAsync(activityProvider, cancellationToken);
    }

    public async Task RefreshDescriptorsAsync(IActivityProvider activityProvider, CancellationToken cancellationToken = default)
    {
        var providerType = activityProvider.GetType();

        // Get new descriptors from provider
        var descriptors = (await activityProvider.GetDescriptorsAsync(cancellationToken)).ToList();

        lock (_registrationLock)
        {
            foreach (var descriptor in descriptors)
                ApplyModifiers(descriptor);
            var groups = descriptors.ToLookup(d => d.TenantId ?? Tenant.AgnosticTenantId);
            // Providers run in the active tenant. Their snapshot also covers shared descriptors.
            var scopes = groups.Select(g => g.Key).Append(Tenant.AgnosticTenantId).Append(tenantAccessor.TenantId).Distinct().ToArray();
            // Validate the complete provider result before modifying any tenant's bindings.
            foreach (var scope in scopes)
            {
                var group = groups[scope];
                var registry = GetOrCreateRegistry(scope);
                registry.ProvidedActivityDescriptors.TryGetValue(providerType, out var owned);
                var keys = new HashSet<(string, int)>();
                foreach (var descriptor in group)
                {
                    if (!keys.Add((descriptor.TypeName, descriptor.Version)))
                        throw ConflictingBinding(descriptor);
                    ValidateBinding(registry, owned, descriptor);
                }
            }

            foreach (var scope in scopes)
            {
                var group = groups[scope];
                var registry = GetOrCreateRegistry(scope);
                var owned = GetOrCreateProviderDescriptors(registry, providerType);
                var keys = group.Select(d => (d.TypeName, d.Version)).ToHashSet();
                foreach (var stale in owned.Where(d => !keys.Contains((d.TypeName, d.Version))).ToList())
                {
                    owned.Remove(stale);
                    RemoveDescriptor(registry, stale);
                }
                foreach (var descriptor in group)
                    StoreDescriptor(registry, owned, descriptor);
            }
        }
    }

    /// <inheritdoc />
    public async Task EnsureDescriptorsAsync(IActivityProvider activityProvider, CancellationToken cancellationToken = default)
    {
        if (activityProvider is not ITenantAgnosticActivityProvider)
        {
            await RefreshDescriptorsAsync(activityProvider, cancellationToken);
            return;
        }

        var providerType = activityProvider.GetType();
        if (_initializedProviders.ContainsKey(providerType))
            return;

        var initializationLock = _providerInitializationLocks.GetOrAdd(providerType, _ => new(1, 1));
        await initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_initializedProviders.ContainsKey(providerType))
                return;

            await RefreshDescriptorsAsync(activityProvider, cancellationToken);
            _initializedProviders.TryAdd(providerType, 0);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private void ApplyModifiers(ActivityDescriptor descriptor)
    {
        foreach (var modifier in modifiers)
            modifier.Modify(descriptor);
    }

    private void ValidateBinding(TenantRegistryData registry, ICollection<ActivityDescriptor>? owned, ActivityDescriptor descriptor)
    {
        if (!registry.ActivityDescriptors.TryGetValue((descriptor.TypeName, descriptor.Version), out var existing))
            return;

        // Workflow-as-activity descriptors share a CLR implementation across logical definitions.
        existing.CustomProperties.TryGetValue("WorkflowDefinitionId", out var existingOwner);
        descriptor.CustomProperties.TryGetValue("WorkflowDefinitionId", out var incomingOwner);
        if (existing.ClrType != descriptor.ClrType || !Equals(existingOwner, incomingOwner) ||
            (owned?.Contains(existing) != true && !_manualActivityDescriptors.Contains(existing)))
            throw ConflictingBinding(descriptor);
    }

    private InvalidOperationException ConflictingBinding(ActivityDescriptor descriptor)
    {
        logger.LogError("Conflicting activity binding for {ActivityType} v{Version} in tenant {TenantId}", descriptor.TypeName, descriptor.Version, descriptor.TenantId);
        return new InvalidOperationException($"Conflicting activity binding for '{descriptor.TypeName}' version {descriptor.Version} in tenant '{descriptor.TenantId ?? Tenant.AgnosticTenantId}'.");
    }

    private void StoreDescriptor(TenantRegistryData registry, ICollection<ActivityDescriptor> owned, ActivityDescriptor descriptor)
    {
        var key = (descriptor.TypeName, descriptor.Version);
        if (registry.ActivityDescriptors.TryGetValue(key, out var existing))
        {
            owned.Remove(existing);
            _manualActivityDescriptors.Remove(existing);
        }
        registry.ActivityDescriptors[key] = descriptor;
        UpdateLatestDescriptor(registry, descriptor);
        owned.Add(descriptor);
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_registrationLock)
        {
            _manualActivityDescriptors.Clear();
            _tenantRegistries.Clear();
            _agnosticRegistry.ActivityDescriptors.Clear();
            _agnosticRegistry.LatestActivityDescriptors.Clear();
            _agnosticRegistry.ProvidedActivityDescriptors.Clear();
            _initializedProviders.Clear();
        }
    }

    /// <inheritdoc />
    public void ClearProvider(Type providerType)
    {
        lock (_registrationLock)
        {
            _initializedProviders.TryRemove(providerType, out _);
            var currentTenantId = tenantAccessor.TenantId;

            // Clear from current tenant's registry
            if (_tenantRegistries.TryGetValue(currentTenantId, out var tenantRegistry)
                && tenantRegistry.ProvidedActivityDescriptors.TryGetValue(providerType, out var descriptors))
            {
                foreach (var descriptor in descriptors.ToList())
                    RemoveDescriptor(tenantRegistry, descriptor);

                tenantRegistry.ProvidedActivityDescriptors.TryRemove(providerType, out _);
            }

            // Clear from agnostic registry
            if (_agnosticRegistry.ProvidedActivityDescriptors.TryGetValue(providerType, out var agnosticDescriptors))
            {
                foreach (var descriptor in agnosticDescriptors.ToList())
                    RemoveDescriptor(_agnosticRegistry, descriptor);

                _agnosticRegistry.ProvidedActivityDescriptors.TryRemove(providerType, out _);
            }
        }
    }

    /// <summary>
    /// Clears all activity descriptors for a specific tenant. Useful when a tenant is deactivated.
    /// </summary>
    internal void ClearTenant(string tenantId)
    {
        lock (_registrationLock)
        {
            _tenantRegistries.TryRemove(tenantId, out _);
        }
    }

    private TenantRegistryData GetOrCreateRegistry(string? tenantId)
    {
        // Null or agnostic tenant ID goes to agnostic registry
        if (tenantId is null or Tenant.AgnosticTenantId)
            return _agnosticRegistry;

        // Get or create tenant-specific registry
        return _tenantRegistries.GetOrAdd(tenantId, _ => new());
    }

    private ICollection<ActivityDescriptor> GetOrCreateProviderDescriptors(TenantRegistryData registry, Type providerType)
    {
        return registry.ProvidedActivityDescriptors.GetOrAdd(providerType, _ => new HashSet<ActivityDescriptor>());
    }

    private static void UpdateLatestDescriptor(TenantRegistryData registry, ActivityDescriptor descriptor)
    {
        registry.LatestActivityDescriptors.AddOrUpdate(
            descriptor.TypeName,
            descriptor,
            (_, latestDescriptor) => descriptor.Version >= latestDescriptor.Version ? descriptor : latestDescriptor);
    }

    private static void RemoveDescriptor(TenantRegistryData registry, ActivityDescriptor descriptor)
    {
        if (!registry.ActivityDescriptors.TryRemove((descriptor.TypeName, descriptor.Version), out var removedDescriptor))
            return;

        if (registry.LatestActivityDescriptors.TryGetValue(removedDescriptor.TypeName, out var latestDescriptor) && latestDescriptor.Version == removedDescriptor.Version)
            RecomputeLatestDescriptor(registry, removedDescriptor.TypeName);
    }

    private static void RecomputeLatestDescriptor(TenantRegistryData registry, string typeName)
    {
        ActivityDescriptor? latestDescriptor = null;
        foreach (var descriptor in registry.ActivityDescriptors.Values)
        {
            if (descriptor.TypeName != typeName)
                continue;

            if (latestDescriptor == null || descriptor.Version > latestDescriptor.Version)
                latestDescriptor = descriptor;
        }

        if (latestDescriptor == null)
            registry.LatestActivityDescriptors.TryRemove(typeName, out _);
        else
            registry.LatestActivityDescriptors[typeName] = latestDescriptor;
    }

}
