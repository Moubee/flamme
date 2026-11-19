using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Models;

namespace Flamme.ExternalAuthentication.Permissions;

public sealed class ClaimMappingPermissionGrantSource : IPermissionGrantSource
{
    public const string SourceType = "claim-mapping";
    public string Type => SourceType;

    public PermissionGrantSourceDescriptor Describe() => new(Type, "Claim mapping", "Grants explicitly mapped Flamme permissions for projected external claims.", 1, BuiltInPermissionGrantSourceDescriptors.Mapping(""), null);

    public ValueTask<PermissionGrantResult> GetGrantsAsync(PermissionGrantContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(CreateResult(context, Type));

    internal static PermissionGrantResult CreateResult(PermissionGrantContext context, string sourceType)
    {
        var grants = PermissionGrantMappingSettings.Read(context.Selection.Settings)
            .SelectMany(mapping => context.ProjectedClaims.TryGetValue(mapping.ClaimType, out var values) && values.Contains(mapping.Value, StringComparer.Ordinal)
                ? mapping.Permissions.Select(permission => new PermissionGrant(permission, sourceType, $"{mapping.ClaimType}:{mapping.Value}"))
                : [])
            .ToArray();
        return new(grants, []);
    }
}
