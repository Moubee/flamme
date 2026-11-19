using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Models;
using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;

namespace Flamme.ExternalAuthentication.Services;

/// <summary>Prevents deleting users that still own external identity links.</summary>
public sealed class ExternalAuthenticationUserDeletionDependencyContributor(
    IExternalIdentityLinkManagementStore links) : IUserDeletionDependencyContributor
{
    public const string SourceName = "external-authentication";
    public string Source => SourceName;

    /// <inheritdoc />
    public async ValueTask<UserDeletionDependency?> InspectAsync(User user, CancellationToken cancellationToken = default)
    {
        var linksForUser = await links.FindAsync(new()
        {
            TenantId = user.TenantId ?? string.Empty,
            UserId = user.Id
        }, cancellationToken);

        return linksForUser.Items.Count == 0
            ? null
            : new UserDeletionDependency(Source, "The user is referenced by one or more external identity links.");
    }
}
