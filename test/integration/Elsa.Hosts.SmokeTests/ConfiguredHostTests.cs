using System.Net;
using System.Security.Cryptography;
using Elsa.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace Elsa.Hosts.SmokeTests;

public class ConfiguredHostTests : WebApplicationTest<ConfiguredHostFactory, ConfiguredHostTests>
{
    protected override void ConfigureTestOptions(WebApplicationTestOptions options) =>
        options.AutoPropagateHttpClientFactory = false;

    [Test]
    public async Task ConfiguredApiRoutesChallengeAnonymousRequests()
    {
        string[] routes =
        [
            "/elsa/api/workflow-definitions",
            "/elsa/api/workflow-instances",
            "/elsa/api/identity/roles",
            "/elsa/api/identity/permissions",
            "/elsa/api/dashboard/overview"
        ];

        using var client = Factory.CreateClient();
        using var assertions = Assert.Multiple();
        foreach (var route in routes)
        {
            using var response = await client.GetAsync(route);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized)
                .Because($"GET {route} must challenge an anonymous request.");
        }
    }
}

public sealed class ConfiguredHostFactory : TestWebApplicationFactory<ConfiguredHostTests>
{
    protected override IHostBuilder CreateHostBuilder() => new HostBuilder();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.ConfigureElsa(elsa => elsa
                    .UseIdentity(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
                    .UseDefaultAuthentication()
                    .UseWorkflowsApi()
                    .UseDashboardApi()).Apply();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(routes => routes.MapWorkflowsApi());
            });
    }
}
