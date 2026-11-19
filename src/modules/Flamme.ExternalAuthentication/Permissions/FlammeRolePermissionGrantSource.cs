using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Models;
using Flamme.Identity.Contracts;
using Flamme.Identity.Models;

namespace Flamme.ExternalAuthentication.Permissions;

public sealed class FlammeRolePermissionGrantSource(IUserProvider userProvider, IRoleProvider roleProvider) : IPermissionGrantSource
{
    public const string SourceType = "elsa-roles";
    public string Type => SourceType;

    public PermissionGrantSourceDescriptor Describe() => new(Type, "Flamme roles", "Grants the permissions assigned to the Flamme user's current roles.", 1, [], null);

    public async ValueTask<PermissionGrantResult> GetGrantsAsync(PermissionGrantContext context, CancellationToken cancellationToken = default)
    {
        var user = await userProvider.FindAsync(new()
            { Id = context.UserId }, cancellationToken);
        if (user is null || !string.Equals(user.TenantId, context.TargetTenantId, StringComparison.Ordinal))
            return new([], [new("user_not_available", "The Flamme user is not available in the target tenant.")]);

        var roles = await roleProvider.FindManyAsync(new()
            { Ids = user.Roles.ToArray() }, cancellationToken);
        var grants = roles
            .Where(x => string.Equals(x.TenantId, context.TargetTenantId, StringComparison.Ordinal))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .SelectMany(role => role.Permissions.Where(permission => !string.IsNullOrWhiteSpace(permission)).OrderBy(permission => permission, StringComparer.Ordinal).Select(permission => new PermissionGrant(permission, Type, role.Id)))
            .ToArray();
        return new(grants, []);
    }
}
