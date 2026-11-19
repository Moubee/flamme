using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Models;

namespace Flamme.ExternalAuthentication.Permissions;

public sealed class GroupMappingPermissionGrantSource : IPermissionGrantSource
{
    public const string SourceType = "group-mapping";
    public string Type => SourceType;

    public PermissionGrantSourceDescriptor Describe() => new(Type, "Group mapping", "Grants explicitly mapped Flamme permissions for projected external group claims.", 1, BuiltInPermissionGrantSourceDescriptors.Mapping("groups"), null);

    public ValueTask<PermissionGrantResult> GetGrantsAsync(PermissionGrantContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(ClaimMappingPermissionGrantSource.CreateResult(context, Type));
}
