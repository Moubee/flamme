using Flamme.Abstractions;
using Flamme.ExternalAuthentication.Constants;
using Flamme.ExternalAuthentication.Services;
using Microsoft.AspNetCore.Builder;

namespace Flamme.ExternalAuthentication.Endpoints.Broker;

internal sealed class CompleteLogout(IExternalAuthenticationBroker broker) : FlammeEndpoint<CompleteLogoutRequest>
{
    public override void Configure()
    {
        Get("/external-authentication/logout/callback/{connectionKey}");
        AllowAnonymous();
        Options(x => x.RequireRateLimiting(ExternalAuthenticationRateLimitPolicyNames.ProviderCallback));
    }

    public override async Task HandleAsync(CompleteLogoutRequest request, CancellationToken cancellationToken)
    {
        var result = await broker.CompleteLogoutAsync(Route<string>("connectionKey") ?? request.ConnectionKey ?? string.Empty, Query<string>("state", false) ?? request.State ?? string.Empty, cancellationToken);
        if (result.Error is { } error)
        {
            await BrokerEndpointSupport.SendErrorAsync(Send, error, cancellationToken);
            return;
        }

        await Send.RedirectAsync(result.RedirectUri!.ToString(), false, true);
    }
}

internal sealed class CompleteLogoutRequest
{
    public string? ConnectionKey { get; set; }
    public string? State { get; set; }
}
