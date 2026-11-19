using Flamme.Api.Client.Resources.ExternalAuthentication.IdentityLinks.Models;

namespace Flamme.Api.Client.Resources.ExternalAuthentication.IdentityLinks.Responses;

/// <summary>
/// A cursor-paged external identity link response.
/// </summary>
public record ListExternalIdentityLinksResponse(IReadOnlyCollection<ExternalIdentityLink> Items, string? NextCursor);
