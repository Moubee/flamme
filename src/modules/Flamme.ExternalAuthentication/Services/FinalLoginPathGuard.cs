using System.Security.Claims;
using Flamme.Authorization;
using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Models;
using Flamme.ExternalAuthentication.Options;
using Flamme.ExternalAuthentication.Permissions;
using Microsoft.Extensions.Options;

namespace Flamme.ExternalAuthentication.Services;

/// <summary>Prevents a management edit from removing the final ordinary sign-in path by accident.</summary>
public sealed class FinalLoginPathGuard(IIdentityProviderConnectionRegistry registry, IOptions<ExternalAuthenticationOptions> options, IPermissionEvaluator permissionEvaluator)
{
    public async ValueTask<FinalLoginPathGuardResult> AuthorizeAsync(IdentityProviderConnection existing, IdentityProviderConnection candidate, string targetTenantId, ClaimsPrincipal actor, bool confirmedOverride, CancellationToken cancellationToken = default)
    {
        var guard = options.Value.FinalLoginPathGuard;
        if (!guard.IsEnabled || !guard.RequireRecoveryMethod || !IsNormal(existing) || IsNormal(candidate) || guard.HasBreakGlassAuthentication || options.Value.LocalLogin.IsEnabled)
            return FinalLoginPathGuardResult.Allowed;
        var effective = await registry.GetAsync(targetTenantId, cancellationToken);
        if (effective.Connections.Any(x => !string.Equals(x.Connection.Id, existing.Id, StringComparison.Ordinal) && IsNormal(x.Connection) && !x.IsShadowed && x.Validity != ConnectionValidity.Invalid))
            return FinalLoginPathGuardResult.Allowed;
        var canOverride = confirmedOverride && permissionEvaluator.HasPermission(actor, guard.PrivilegedOverridePermission);
        return canOverride ? FinalLoginPathGuardResult.Allowed : FinalLoginPathGuardResult.Denied;
    }

    private static bool IsNormal(IdentityProviderConnection connection) => connection is { IsEnabled: true, ArchivedAt: null };
}

public enum FinalLoginPathGuardResult { Allowed, Denied }
