using Flamme.Common.Multitenancy;

namespace Flamme.Tenants.AspNetCore;

public static class TenantExtensions
{
    public static string? GetRoutePrefix(this Tenant tenant)
    {
        return tenant.Configuration.GetSection("Http")["Prefix"];
    }
    
    public static string? GetHost(this Tenant tenant)
    {
        return tenant.Configuration.GetSection("Http")["Host"];
    }
}