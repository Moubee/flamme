using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flamme.Common.DistributedHosting;
using Flamme.Common.Multitenancy;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Runtime.Activities;
using Medallion.Threading;
using Microsoft.Extensions.Options;

namespace Flamme.Workflows.Runtime.Authoring;

/// <summary>
/// Compiles an authorized author's source document against a curated catalog before publishing a
/// new JSON workflow revision. Execution and resume require only the ordinary built-in activities.
/// </summary>
public sealed class CuratedWorkflowPublisher(
    IWorkflowDefinitionPublisher publisher,
    IWorkflowDefinitionStore store,
    IActivityRegistry registry,
    ITenantAccessor tenantAccessor,
    IDistributedLockProvider locks,
    IOptions<DistributedLockingOptions> lockOptions)
{
    /// <summary>Definition custom property carrying the release pins and occurrence-to-activity mapping.</summary>
    public const string ManifestProperty = "flamme.authoring";

    public async Task<WorkflowDefinition> PublishAsync(ProducerCatalog catalog, AuthoredWorkflow source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.DefinitionId);
        var (root, manifest) = Compile(catalog, source);
        foreach (var activity in root.Activities.Prepend(root).DistinctBy(x => (x.Type, x.Version)))
        {
            var descriptor = registry.Find(activity.Type, activity.Version);
            if (descriptor?.ClrType != activity.GetType())
                throw new InvalidOperationException($"The host must register built-in '{activity.Type}' major {activity.Version}.");
        }

        var revision = await publisher.NewAsync(root, cancellationToken);
        revision.DefinitionId = source.DefinitionId;
        revision.Name = source.DefinitionId;
        revision.IsReadonly = true;
        revision.CustomProperties[ManifestProperty] = manifest;

        var lockName = "flamme:curated-publication:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.DefinitionId)));
        await using var handle = await locks.AcquireLockAsync(lockName, lockOptions.Value.LockAcquisitionTimeout, cancellationToken);
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
        var previous = await store.FindLastVersionAsync(new() { DefinitionId = source.DefinitionId }, lease.Token);
        if (previous != null && !TenantVisibility.CanReplace(previous.TenantId, tenantAccessor.TenantId))
            throw new InvalidOperationException($"The current tenant cannot replace the published workflow '{source.DefinitionId}'.");
        if (previous != null && !previous.CustomProperties.ContainsKey(ManifestProperty))
            throw new InvalidOperationException($"'{source.DefinitionId}' already identifies a workflow outside the curated catalog.");

        // Even a retracted publication may still have live bookmarks. Always append a new ID.
        revision.Version = checked((previous?.Version ?? 0) + 1);
        var result = await publisher.PublishAsync(revision, lease.Token);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.ValidationErrors.Select(x => x.Message)));
        return revision;
    }

    internal static (Sequence Root, JsonElement Manifest) Compile(ProducerCatalog catalog, AuthoredWorkflow source)
    {
        ArgumentNullException.ThrowIfNull(source.Imports);
        ArgumentNullException.ThrowIfNull(source.Calls);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var import in source.Imports)
        {
            if (!ProducerCatalog.IsIdentifier(import.Alias) || !aliases.TryAdd(import.Alias, import.Namespace))
                throw new ArgumentException($"Invalid or duplicate import alias '{import.Alias}'.", nameof(source));
            if (!catalog.HasNamespace(import.Namespace))
                throw new KeyNotFoundException($"Unknown producer namespace '{import.Namespace}'.");
        }

        var root = new Sequence(source: null, line: null) { Id = "plan" };
        var occurrences = new HashSet<string>(StringComparer.Ordinal);
        var releases = new Dictionary<(string Key, int Major), ProducerCatalog.AdmittedRecipe>();
        var sourceMap = new List<object>();
        foreach (var call in source.Calls)
        {
            if (!ProducerCatalog.IsIdentifier(call.Id) || !occurrences.Add(call.Id))
                throw new ArgumentException($"Invalid or duplicate source occurrence '{call.Id}'.", nameof(source));
            var parts = call.Name.Split(':');
            if (parts.Length != 2 || !ProducerCatalog.IsIdentifier(parts[1]) || !aliases.TryGetValue(parts[0], out var scope) || call.Major < 1)
                throw new ArgumentException($"Invalid or unbound call '{call.Name}' major {call.Major}.", nameof(source));

            var release = catalog.Resolve($"{scope}.{parts[1]}", call.Major);
            releases.TryAdd((release.Key, release.Major), release);
            foreach (var step in release.Steps)
            {
                IActivity activity = step.Primitive switch
                {
                    RecipePrimitive.WriteLine => new WriteLine(step.Value, source: null, line: null),
                    RecipePrimitive.WaitForEvent => new Event(step.Value, source: null, line: null),
                    _ => throw new InvalidOperationException($"Unsupported recipe primitive '{step.Primitive}'.")
                };
                // Neither validated component can contain '.', so this mapping is injective.
                activity.Id = $"{call.Id}.{step.Id}";
                root.Activities.Add(activity);
                sourceMap.Add(new { occurrence = call.Id, call = call.Name, release.Key, release.Major, role = step.Id,
                    activityId = activity.Id, primitive = activity.Type, version = activity.Version });
            }
        }

        var manifest = JsonSerializer.SerializeToElement(new { format = 1, releases = releases.Values.ToArray(), sourceMap }, JsonSerializerOptions.Web);
        return (root, manifest);
    }
}
