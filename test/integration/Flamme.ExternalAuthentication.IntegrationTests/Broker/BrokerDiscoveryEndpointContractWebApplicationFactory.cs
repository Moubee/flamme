using System.Reflection;
using Flamme.ExternalAuthentication.Features;
using Flamme.ExternalAuthentication.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace Flamme.ExternalAuthentication.IntegrationTests.Broker;

public sealed class BrokerDiscoveryEndpointContractWebApplicationFactory : TestWebApplicationFactory<ExternalAuthenticationTestEntryPoint>
{
    protected override IHostBuilder CreateHostBuilder() => new HostBuilder();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        ExternalAuthenticationTestHost.Configure(builder, typeof(ExternalAuthenticationFeature).Assembly, endpoint => endpoint.Namespace == "Flamme.ExternalAuthentication.Endpoints.Broker", addRateLimiter: true);
    }
}
