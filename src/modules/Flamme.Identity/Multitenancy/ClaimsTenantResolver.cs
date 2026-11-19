using Flamme.Common.Multitenancy;
using Flamme.Identity.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Flamme.Identity.Multitenancy;

/// <summary>
/// Resolves the tenant from the user's claims.
/// </summary>
public class ClaimsTenantResolver(IHttpContextAccessor httpContextAccessor, IOptions<IdentityTokenOptions> options) : TenantResolverBase
{
    /// <inheritdoc />
    protected override TenantResolverResult Resolve(TenantResolverContext context)
    {
        var httpContext = httpContextAccessor.HttpContext;

        if (httpContext == null)
            return Unresolved();

        var tenantId = httpContext.User.FindFirst(options.Value.TenantIdClaimsType)?.Value;
        return AutoResolve(tenantId);
    }
}