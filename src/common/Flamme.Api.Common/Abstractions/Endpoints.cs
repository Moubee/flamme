using FastEndpoints;

namespace Flamme.Abstractions;

/// <summary>
/// An endpoint that maps a request to a response.
/// </summary>
public abstract class FlammeEndpointWithMapper<TRequest, TMapper> : EndpointWithMapper<TRequest, TMapper> where TMapper : class, IRequestMapper where TRequest : notnull
{
    /// <summary>Requires a permission satisfying <paramref name="resource"/> and <paramref name="verb"/>.</summary>
    protected void RequirePermission(string resource, string verb) => EndpointSecurity.RequirePermission(Definition, resource, verb);

    /// <summary>Requires a permission satisfying any one of <paramref name="permissions"/>.</summary>
    protected void RequireAnyPermission(params (string Resource, string Verb)[] permissions) => EndpointSecurity.RequireAnyPermission(Definition, permissions);

    /// <summary>Requires an authenticated caller but no permission.</summary>
    protected void RequireAuthenticatedOnly() => EndpointSecurity.RequireAuthenticatedOnly(Definition);

    /// <summary>Declares required permissions as legacy strings.</summary>
    protected void ConfigurePermissions(params string[] permissions) => EndpointSecurity.ConfigurePermissions(Definition, permissions);
}

public abstract class FlammeEndpointWithoutRequest : EndpointWithoutRequest
{
    /// <summary>Requires a permission satisfying <paramref name="resource"/> and <paramref name="verb"/>.</summary>
    protected void RequirePermission(string resource, string verb) => EndpointSecurity.RequirePermission(Definition, resource, verb);

    /// <summary>Requires a permission satisfying any one of <paramref name="permissions"/>.</summary>
    protected void RequireAnyPermission(params (string Resource, string Verb)[] permissions) => EndpointSecurity.RequireAnyPermission(Definition, permissions);

    /// <summary>Requires an authenticated caller but no permission.</summary>
    protected void RequireAuthenticatedOnly() => EndpointSecurity.RequireAuthenticatedOnly(Definition);

    /// <summary>Declares required permissions as legacy strings.</summary>
    protected void ConfigurePermissions(params string[] permissions) => EndpointSecurity.ConfigurePermissions(Definition, permissions);
}

public abstract class FlammeEndpointWithoutRequest<TResponse> : EndpointWithoutRequest<TResponse> where TResponse : notnull
{
    /// <summary>Requires a permission satisfying <paramref name="resource"/> and <paramref name="verb"/>.</summary>
    protected void RequirePermission(string resource, string verb) => EndpointSecurity.RequirePermission(Definition, resource, verb);

    /// <summary>Requires a permission satisfying any one of <paramref name="permissions"/>.</summary>
    protected void RequireAnyPermission(params (string Resource, string Verb)[] permissions) => EndpointSecurity.RequireAnyPermission(Definition, permissions);

    /// <summary>Requires an authenticated caller but no permission.</summary>
    protected void RequireAuthenticatedOnly() => EndpointSecurity.RequireAuthenticatedOnly(Definition);

    /// <summary>Declares required permissions as legacy strings.</summary>
    protected void ConfigurePermissions(params string[] permissions) => EndpointSecurity.ConfigurePermissions(Definition, permissions);
}

public class FlammeEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse> where TRequest : notnull where TResponse : notnull
{
    /// <summary>Requires a permission satisfying <paramref name="resource"/> and <paramref name="verb"/>.</summary>
    protected void RequirePermission(string resource, string verb) => EndpointSecurity.RequirePermission(Definition, resource, verb);

    /// <summary>Requires a permission satisfying any one of <paramref name="permissions"/>.</summary>
    protected void RequireAnyPermission(params (string Resource, string Verb)[] permissions) => EndpointSecurity.RequireAnyPermission(Definition, permissions);

    /// <summary>Requires an authenticated caller but no permission.</summary>
    protected void RequireAuthenticatedOnly() => EndpointSecurity.RequireAuthenticatedOnly(Definition);

    /// <summary>Declares required permissions as legacy strings.</summary>
    protected void ConfigurePermissions(params string[] permissions) => EndpointSecurity.ConfigurePermissions(Definition, permissions);
}

public class FlammeEndpoint<TRequest, TResponse, TMapper> : Endpoint<TRequest, TResponse, TMapper> where TRequest : notnull where TResponse : notnull where TMapper : class, IMapper, new()
{
    /// <summary>Requires a permission satisfying <paramref name="resource"/> and <paramref name="verb"/>.</summary>
    protected void RequirePermission(string resource, string verb) => EndpointSecurity.RequirePermission(Definition, resource, verb);

    /// <summary>Requires a permission satisfying any one of <paramref name="permissions"/>.</summary>
    protected void RequireAnyPermission(params (string Resource, string Verb)[] permissions) => EndpointSecurity.RequireAnyPermission(Definition, permissions);

    /// <summary>Requires an authenticated caller but no permission.</summary>
    protected void RequireAuthenticatedOnly() => EndpointSecurity.RequireAuthenticatedOnly(Definition);

    /// <summary>Declares required permissions as legacy strings.</summary>
    protected void ConfigurePermissions(params string[] permissions) => EndpointSecurity.ConfigurePermissions(Definition, permissions);
}

public class FlammeEndpoint<TRequest> : Endpoint<TRequest> where TRequest : notnull
{
    /// <summary>Requires a permission satisfying <paramref name="resource"/> and <paramref name="verb"/>.</summary>
    protected void RequirePermission(string resource, string verb) => EndpointSecurity.RequirePermission(Definition, resource, verb);

    /// <summary>Requires a permission satisfying any one of <paramref name="permissions"/>.</summary>
    protected void RequireAnyPermission(params (string Resource, string Verb)[] permissions) => EndpointSecurity.RequireAnyPermission(Definition, permissions);

    /// <summary>Requires an authenticated caller but no permission.</summary>
    protected void RequireAuthenticatedOnly() => EndpointSecurity.RequireAuthenticatedOnly(Definition);

    /// <summary>Declares required permissions as legacy strings.</summary>
    protected void ConfigurePermissions(params string[] permissions) => EndpointSecurity.ConfigurePermissions(Definition, permissions);
}