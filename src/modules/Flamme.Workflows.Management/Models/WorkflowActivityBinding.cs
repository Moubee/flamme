using Flamme.Common.Multitenancy;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Exceptions;

namespace Flamme.Workflows.Management.Models;

/// <summary>The tenant-scoped descriptor key claimed by a native workflow revision.</summary>
public readonly record struct WorkflowActivityBinding(string TenantId, string TypeName, int Version)
{
    public static WorkflowActivityBinding? From(string? tenantId, string? name, int version) =>
        string.IsNullOrWhiteSpace(name) ? null : new(tenantId.NormalizeTenantId(), WorkflowDefinitionActivityDescriptorFactory.GetTypeName(name), version);

    public void Claim(Dictionary<WorkflowActivityBinding, string> owners, string definitionId)
    {
        ValidateOwner(owners, definitionId);
        owners[this] = definitionId;
    }

    public void ValidateOwner(IReadOnlyDictionary<WorkflowActivityBinding, string> owners, string definitionId)
    {
        if (owners.TryGetValue(this, out var owner) && owner != definitionId)
            throw new WorkflowDefinitionValidationException($"Another workflow already provides '{TypeName}' version {Version} in tenant '{TenantId}'.");
    }
}
