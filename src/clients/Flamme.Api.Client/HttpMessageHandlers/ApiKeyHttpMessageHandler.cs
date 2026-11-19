using System.Net.Http.Headers;
using Flamme.Api.Client.Options;
using Microsoft.Extensions.Options;

namespace Flamme.Api.Client.HttpMessageHandlers;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that configures the outgoing HTTP request to use an API key as the authorization header.
/// </summary>
public class ApiKeyHttpMessageHandler : DelegatingHandler
{
    private readonly FlammeClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyHttpMessageHandler"/> class.
    /// </summary>
    public ApiKeyHttpMessageHandler(IOptions<FlammeClientOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _options.ApiKey;
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);

        return await base.SendAsync(request, cancellationToken);
    }
}