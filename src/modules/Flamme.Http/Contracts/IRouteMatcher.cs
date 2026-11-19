using Microsoft.AspNetCore.Routing;

namespace Flamme.Http;

/// <summary>
/// Matches a given request path against the specified route template.
/// </summary>
public interface IRouteMatcher
{
    /// <summary>
    /// Matches a given request path against the specified route template.
    /// </summary>
    RouteValueDictionary? Match(string routeTemplate, string route);
}