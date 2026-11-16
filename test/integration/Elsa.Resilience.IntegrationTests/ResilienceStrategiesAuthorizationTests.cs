using System.Net;
using Elsa.Http.Resilience;
using Elsa.Resilience.Options;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace Elsa.Resilience.IntegrationTests;

/// <summary>
/// The designer lists the resilience strategies to open a definition, and they describe what is configured for the
/// host rather than anything a caller stored, so any signed-in user may read them.
/// </summary>
public class ResilienceStrategiesAuthorizationTests : WebApplicationTest<ResilienceStrategiesWebApplicationFactory, ResilienceTestEntryPoint>
{
    private const string Path = "/resilience/strategies";

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        var catalog = IResilienceStrategyCatalog.Mock().Object;
        Mock.Get(catalog).ListAsync(Arg.Any<CancellationToken>()).Returns([new HttpResilienceStrategy()]);
        services.AddSingleton(catalog);
        services.Configure<ResilienceOptions>(options => options.StrategyTypes.Add(typeof(HttpResilienceStrategy)));
    }

    [Test]
    public async Task AuthenticatedUserWithoutAnyPermission_IsAllowed()
    {
        var response = await SendAsync(authenticated: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(nameof(HttpResilienceStrategy));
    }

    [Test]
    public async Task AnonymousCaller_IsRejected()
    {
        var response = await SendAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> SendAsync(bool authenticated = false)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);

        if (authenticated)
        {
            // The handler authenticates only when the header is present; a value that parses to no permission grants nothing.
            request.Headers.Add(TestAuthenticationHandler.PermissionHeader, "no-grants");
        }

        return await client.SendAsync(request);
    }
}
