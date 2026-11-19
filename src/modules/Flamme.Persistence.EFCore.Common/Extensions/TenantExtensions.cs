using Flamme.Common.Multitenancy;

namespace Flamme.Persistence.EFCore.Extensions;

public static class TenantExtensions
{
    public static string? GetConnectionString(this Tenant tenant, string name)
    {
        return tenant.Configuration.GetSection("ConnectionStrings")[name];
    }
}