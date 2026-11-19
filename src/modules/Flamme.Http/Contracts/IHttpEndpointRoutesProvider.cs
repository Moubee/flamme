using Flamme.Http.Contexts;

namespace Flamme.Http;

public interface IHttpEndpointRoutesProvider
{
    Task<IEnumerable<HttpRouteData>> GetRoutesAsync(HttpEndpointRouteProviderContext context);
}