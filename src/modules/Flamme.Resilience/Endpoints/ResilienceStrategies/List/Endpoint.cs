using Flamme.Abstractions;
using Flamme.Models;
using Flamme.Resilience.Serialization;
using Microsoft.AspNetCore.Http;

namespace Flamme.Resilience.Endpoints.ResilienceStrategies.List;

public class Endpoint(IResilienceStrategyCatalog catalog, ResilienceStrategySerializer serializer) : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/resilience/strategies");

        // The configured resilience strategies, which the designer needs to open any definition, so every signed-in user may read it.
        RequireAuthenticatedOnly();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var strategies = (await catalog.ListAsync(ct)).ToList();
        var response = new ListResponse<IResilienceStrategy>(strategies);

        await HttpContext.Response.WriteAsJsonAsync(response, serializer.SerializerOptions, ct);
    }
}