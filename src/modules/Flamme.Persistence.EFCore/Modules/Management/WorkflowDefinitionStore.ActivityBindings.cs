using Flamme.Common.Multitenancy;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Models;
using Microsoft.EntityFrameworkCore;

namespace Flamme.Persistence.EFCore.Modules.Management;

public partial class EFCoreWorkflowDefinitionStore
{
    /// <inheritdoc />
    public bool EnforcesActivityBindingOwnership => true;

    private static async Task ValidateActivityBindingsAsync(ManagementFlammeDbContext db, IReadOnlyCollection<WorkflowDefinition> incoming, CancellationToken cancellationToken)
    {
        // Use persisted fields after the write: EF saving hooks can stamp the tenant, and
        // batch and compare-and-swap operations may update only selected columns.
        var activities = db.WorkflowDefinitions.IgnoreQueryFilters()
            .Where(x => EF.Property<bool?>(x, "UsableAsActivity") == true)
            .Select(x => new { x.Id, x.DefinitionId, x.TenantId, x.Name, x.Version });
        var ids = incoming.Select(x => x.Id).ToArray();
        var written = await activities.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var owners = new Dictionary<WorkflowActivityBinding, string>();
        foreach (var row in written)
            WorkflowActivityBinding.From(row.TenantId, row.Name, row.Version)?.Claim(owners, row.DefinitionId);

        if (owners.Count == 0)
            return;

        // A competing transaction may have inserted the same descriptor under a different
        // revision ID. Serialize claims in a stable order and check committed owners under
        // READ COMMITTED before releasing the transaction locks at commit.
        var resources = owners.Keys.Select(x => FormattableString.Invariant($"workflow-activity:{db.Schema}:{x.TenantId}:{x.TypeName}:{x.Version}"));
        foreach (var resource in resources.Order(StringComparer.Ordinal))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellationToken);

        var versions = owners.Keys.Select(x => x.Version).Distinct().ToArray();
        var tenants = owners.Keys.Select(x => x.TenantId).Distinct().ToArray();
        var candidates = await activities.Where(x => versions.Contains(x.Version) && tenants.Contains(x.TenantId ?? Tenant.DefaultTenantId)).ToListAsync(cancellationToken);
        foreach (var row in candidates)
            WorkflowActivityBinding.From(row.TenantId, row.Name, row.Version)?.ValidateOwner(owners, row.DefinitionId);
    }
}
