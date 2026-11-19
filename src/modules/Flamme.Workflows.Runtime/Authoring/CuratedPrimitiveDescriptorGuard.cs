using Flamme.Common.Multitenancy;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.Runtime.Authoring;

/// <summary>Reserves the executable contracts used by curated publications on every runtime host.</summary>
internal sealed class CuratedPrimitiveDescriptorGuard : IActivityDescriptorModifier
{
    private static readonly IReadOnlyDictionary<string, Type> Primitives =
        new[] { typeof(Sequence), typeof(WriteLine), typeof(Event) }.ToDictionary(ActivityTypeNameHelper.GenerateTypeName);

    public void Modify(ActivityDescriptor descriptor)
    {
        if (!Primitives.TryGetValue(descriptor.TypeName, out var primitive))
            return;

        if (descriptor.TenantId is not (null or Tenant.AgnosticTenantId)
            || descriptor.ClrType != primitive || descriptor.Version != ActivityTypeNameHelper.GetVersion(primitive))
            throw new InvalidOperationException($"'{descriptor.TypeName}' is a reserved, tenant-agnostic primitive implemented by '{primitive.FullName}'.");
    }
}
