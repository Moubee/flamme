namespace Flamme.Identity.Models;

/// <summary>
/// Represents an issued Flamme token.
/// </summary>
/// <param name="Token">The serialized token.</param>
/// <param name="ExpiresAt">The token expiration time.</param>
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
