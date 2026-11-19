using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Identity.Contracts;
using Flamme.Identity.Permissions;
using JetBrains.Annotations;

namespace Flamme.Identity.Endpoints.Users.Create;

/// <summary>
/// An endpoint that creates a new user.
/// </summary>
[PublicAPI]
internal class Create(IUserManager userManager, IRoleAuthorizationService roleAuthorizationService) : FlammeEndpoint<Request, Response>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/identity/users");
        RequirePermission(IdentityPermissions.Users, CoreVerbs.Create);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        if (!await roleAuthorizationService.CanAssignRolesAsync(User, request.Roles, cancellationToken))
        {
            await Send.ForbiddenAsync(cancellationToken);
            return;
        }

        var result = await userManager.CreateUserAsync(
            request.Name,
            request.Password,
            request.Roles,
            cancellationToken);

        await Send.OkAsync(Response.FromResult(result), cancellationToken);
    }
}
