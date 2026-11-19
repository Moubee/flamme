using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Models;

namespace Flamme.Workflows.Management.Stores;

public partial class MemoryWorkflowDefinitionStore
{
    /// <inheritdoc />
    public bool EnforcesActivityBindingOwnership => true;

    private void ValidateActivityBindings(IReadOnlyCollection<WorkflowDefinition> incoming)
    {
        var owners = new Dictionary<WorkflowActivityBinding, string>();
        foreach (var definition in incoming.Where(x => x.Options.UsableAsActivity == true))
            WorkflowActivityBinding.From(definition.TenantId, definition.Name, definition.Version)?.Claim(owners, definition.DefinitionId);

        if (owners.Count == 0)
            return;

        var replacedIds = incoming.Select(x => x.Id).ToHashSet();
        foreach (var existing in store.List())
        {
            if (replacedIds.Contains(existing.Id) || existing.Options.UsableAsActivity != true)
                continue;

            WorkflowActivityBinding.From(existing.TenantId, existing.Name, existing.Version)?.ValidateOwner(owners, existing.DefinitionId);
        }
    }
}
