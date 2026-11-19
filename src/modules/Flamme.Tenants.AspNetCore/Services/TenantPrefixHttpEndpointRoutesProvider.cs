using Flamme.Common.Multitenancy;
using Flamme.Extensions;
using Flamme.Http;
using Flamme.Http.Contexts;
using Flamme.Http.Options;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Flamme.Tenants.AspNetCore.Services;

/// <summary>
/// Provides a route for an HTTP endpoint based on the specified context and tenant prefix.
/// </summary>
[UsedImplicitly]
public class TenantPrefixHttpEndpointRoutesProvider(ITenantsProvider tenantsProvider, IOptions<HttpActivityOptions> options) : IHttpEndpointRoutesProvider
{
    public async Task<IEnumerable<HttpRouteData>> GetRoutesAsync(HttpEndpointRouteProviderContext context)
    {
        var routes = new List<HttpRouteData>();
        var path = context.Payload.Path;

        if (string.IsNullOrWhiteSpace(path))
            return routes;

        await AddTenantRouteAsync(context, routes);
        AddDefaultRoute(context, routes);
        return routes;
    }

    private async Task AddTenantRouteAsync(HttpEndpointRouteProviderContext context, List<HttpRouteData> routes)
    {
        var segments = new List<string?>();
        var routeDataValues = new RouteValueDictionary();

        if (!string.IsNullOrEmpty(context.TenantId))
        {
            var tenant = await tenantsProvider.FindByIdAsync(context.TenantId);
            var tenantPrefix = tenant?.GetRoutePrefix();

            if (!string.IsNullOrWhiteSpace(tenantPrefix))
            {
                segments.Add("{tenantPrefix}");
                routeDataValues.Add("tenantId", tenant!.Id);
            }
        }

        var path = context.Payload.Path;
        segments.AddRange(options.Value.BasePath.ToString(), path);
        var routeData = new HttpRouteData(segments.JoinSegments(), routeDataValues);
        routes.Add(routeData);
    }

    private void AddDefaultRoute(HttpEndpointRouteProviderContext context, List<HttpRouteData> routes)
    {
        var basePath = options.Value.BasePath.ToString();
        var path = context.Payload.Path;
        var routeData = new HttpRouteData(new[]
        {
            basePath, path
        }.JoinSegments());
        routes.Add(routeData);
    }
}