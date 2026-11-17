using Elsa.CodeGeneration;
using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Features.Services;

/// <summary>
/// Selects versioned exports and merges their typed bindings into a host-local catalog.
/// </summary>
public sealed class FeatureModuleCatalogBuilder
{
    private readonly ModuleExportRegistryBuilder<FeatureModuleExport> _exports = new();

    /// <summary>
    /// Adds an export. Only re-adding the currently selected identical export object is idempotent.
    /// </summary>
    public FeatureModuleCatalogBuilder Add(FeatureModuleExport export)
    {
        _exports.Add(export);
        return this;
    }

    /// <summary>
    /// Replaces an explicitly expected selected export and revalidates the result when built.
    /// </summary>
    public FeatureModuleCatalogBuilder Replace(FeatureModuleExport expected, FeatureModuleExport replacement)
    {
        _exports.Replace(expected, replacement);
        return this;
    }

    /// <summary>
    /// Validates module and binding ownership before constructing the complete feature catalog.
    /// No feature factory or lifecycle callback runs during composition.
    /// </summary>
    public ExplicitFeatureCatalog Build()
    {
        var selected = _exports.Build().Exports;
        var features = new Dictionary<Type, FeatureRegistration>();
        var hostedServices = new Dictionary<Type, ServiceDescriptor>();
        var featureOwners = new Dictionary<Type, FeatureModuleExport>();
        var identityOwners = new Dictionary<string, FeatureModuleExport>(StringComparer.Ordinal);
        var hostedOwners = new Dictionary<Type, FeatureModuleExport>();
        foreach (var export in selected)
        {
            foreach (var feature in export.Features)
            {
                CheckOwnership(featureOwners, feature.Type, export, "feature type");
                CheckOwnership(identityOwners, $"{feature.Namespace}.{feature.Name}", export, "installed feature identity");
                features.Add(feature.Type, feature);
            }
            foreach (var service in export.HostedServices)
            {
                CheckOwnership(hostedOwners, service.Key, export, "hosted-service type");
                hostedServices.Add(service.Key, service.Value);
            }
        }

        Dictionary<string, FeatureModuleExport>? modules = null;
        foreach (var export in selected)
        {
            HashSet<string>? reachable = null;
            foreach (var feature in export.Features)
            {
                foreach (var dependency in feature.Dependencies)
                {
                    if (!featureOwners.TryGetValue(dependency, out var owner))
                        throw new InvalidOperationException($"Module {export.Descriptor.ModuleId} feature {feature.Type} requires missing feature {dependency}.");
                    if (ReferenceEquals(owner, export))
                        continue;

                    var ownerId = owner.Descriptor.ModuleId;
                    if (reachable is null && export.Descriptor.Requirements.Any(requirement => requirement.ModuleId == ownerId))
                        continue;
                    modules ??= selected.ToDictionary(module => module.Descriptor.ModuleId, StringComparer.Ordinal);
                    reachable ??= GetRequiredModules(export, modules);
                    if (!reachable.Contains(ownerId))
                        throw new InvalidOperationException($"Module {export.Descriptor.ModuleId} feature {feature.Type} requires feature {dependency} from module {ownerId}, but that module is not in its declared requirement closure.");
                }
            }
        }

        // Producers validate local cycles. Every cross-export edge follows the acyclic module graph.
        return new ExplicitFeatureCatalog(features, hostedServices);
    }

    private static HashSet<string> GetRequiredModules(FeatureModuleExport export, Dictionary<string, FeatureModuleExport> modules)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<FeatureModuleExport>();
        pending.Push(export);
        while (pending.TryPop(out var current))
        {
            foreach (var requirement in current.Descriptor.Requirements)
            {
                if (reachable.Add(requirement.ModuleId))
                    pending.Push(modules[requirement.ModuleId]);
            }
        }
        return reachable;
    }

    /// <summary>
    /// Validates and composes a host's explicit exports before constructing its module.
    /// </summary>
    public static IModule CreateModule(IServiceCollection services, FeatureModuleExport[] exports, CompositionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (exports is not { Length: > 0 })
            throw new InvalidOperationException("ElsaHost.GetModuleExports must return at least one selected FeatureModuleExport.");

        var builder = new FeatureModuleCatalogBuilder();
        foreach (var export in exports)
            builder.Add(export ?? throw new InvalidOperationException("ElsaHost.GetModuleExports returned a null module export."));

        return new Module(services, builder.Build(), policy);
    }

    private static void CheckOwnership<TKey>(Dictionary<TKey, FeatureModuleExport> owners, TKey key, FeatureModuleExport export, string kind)
        where TKey : notnull
    {
        if (!owners.TryAdd(key, export))
            throw new InvalidOperationException($"Duplicate {kind} {key}: module {owners[key].Descriptor.ModuleId} conflicts with module {export.Descriptor.ModuleId}.");
    }
}
