using System.Security.Claims;
using Flamme.Identity.Entities;

namespace Flamme.Identity.Models;

/// <summary>
/// Contains the trusted Flamme identity data to project into a token.
/// </summary>
/// <param name="User">The Flamme user.</param>
/// <param name="Roles">The effective Flamme roles.</param>
/// <param name="Permissions">The effective Flamme permissions.</param>
/// <param name="AdditionalClaims">Additional trusted claims.</param>
/// <param name="ExternalAuthenticationSessionId">The optional external authentication session ID.</param>
public sealed record TokenIssuanceContext(
    User User,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<Claim> AdditionalClaims,
    string? ExternalAuthenticationSessionId = null)
{
    /// <summary>
    /// The sign-in session a refresh token issued from this context continues. When not set, the refresh token
    /// starts a new session.
    /// </summary>
    public SignInSession? Session { get; init; }
}
