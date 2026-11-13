using Elsa.Resilience.Features;
using Elsa.Resilience.Options;
using Elsa.Resilience.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace Elsa.Resilience.IntegrationTests;

public sealed class ResilienceStrategiesWebApplicationFactory : TestWebApplicationFactory<ResilienceTestEntryPoint>
{
    protected override IHostBuilder CreateHostBuilder() => new HostBuilder();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder
            .UseEnvironment(Environments.Development)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging();
                services.AddFastEndpoints(options =>
                {
                    options.Assemblies = [typeof(ResilienceFeature).Assembly];
                    options.DisableAutoDiscovery = true;
                    options.Filter = endpoint => endpoint.Namespace == "Elsa.Resilience.Endpoints.ResilienceStrategies.List";
                });
                services.AddOptions<ResilienceOptions>();
                services.AddSingleton<ResilienceStrategySerializer>();
                services.AddAuthentication(TestAuthenticationHandler.AuthenticationScheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.AuthenticationScheme, _ => { });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapFastEndpoints());
            });
    }
}
