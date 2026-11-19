using Flamme.Api.Client.Resources.ExternalAuthentication.IdentityLinks.Models;

namespace Flamme.Api.Client.Resources.ExternalAuthentication.IdentityLinks.Responses;

/// <summary>
/// A cursor-paged minimal user picker response.
/// </summary>
public record FindIdentityLinkUsersResponse(IReadOnlyCollection<IdentityLinkUser> Items, string? NextCursor);
