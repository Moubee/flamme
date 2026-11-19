using System.Text.Json.Serialization;
using Flamme.Workflows.Attributes;

namespace Flamme.Http.Bookmarks;

/// <summary>
/// A bookmark used by the <see cref="HttpEndpoint"/> activity.
/// </summary>
public record HttpEndpointBookmarkPayload
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpEndpointBookmarkPayload"/> class.
    /// </summary>
    [JsonConstructor]
    public HttpEndpointBookmarkPayload()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpEndpointBookmarkPayload"/> class.
    /// </summary>
    public HttpEndpointBookmarkPayload(string path, string method, bool? authorize = default, string? policy = default, TimeSpan? requestTimeout = default, long? requestSizeLimit = default)
    {
        Path = path;
        Method = method;
        Authorize = authorize;
        Policy = policy;
        RequestTimeout = requestTimeout;
        RequestSizeLimit = requestSizeLimit;
    }

    /// <summary>
    /// Gets or sets the path of the HTTP endpoint.
    /// </summary>
    public string Path
    {
        get;
        init => field = value.ToLowerInvariant();
    } = default!;

    /// <summary>
    /// Gets or sets the HTTP method of the endpoint.
    /// </summary>
    public string Method
    {
        get;
        init => field = value.ToLowerInvariant();
    } = default!;

    /// <summary>
    /// Gets or sets the policy to use for authorization.
    /// </summary>
    [ExcludeFromHash] public string? Policy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the endpoint requires authorization.
    /// </summary>
    [ExcludeFromHash] public bool? Authorize { get; set; }
    
    /// <summary>
    /// Gets or sets the request timeout.
    /// </summary>
    [ExcludeFromHash] public TimeSpan? RequestTimeout { get; set; }
    
    /// <summary>
    /// Gets or sets the max request size in bytes.
    /// </summary>
    [ExcludeFromHash] public long? RequestSizeLimit { get; set; }
}
