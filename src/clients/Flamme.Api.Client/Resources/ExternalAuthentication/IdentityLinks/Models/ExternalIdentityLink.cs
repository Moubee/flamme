namespace Flamme.Api.Client.Resources.ExternalAuthentication.IdentityLinks.Models;

/// <summary>
/// Safe metadata for an external identity linked to a Flamme user.
/// </summary>
public record ExternalIdentityLink(
    string Id,
    string UserId,
    string ConnectionKey,
    string Issuer,
    string? SubjectHint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignedInAt);
