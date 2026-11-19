using System.Reflection;
using Flamme.ExternalAuthentication.IntegrationTests.Fixtures;
using Flamme.Identity.Constants;
using Flamme.Identity.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace Flamme.ExternalAuthentication.IntegrationTests.Compatibility;

public sealed class LegacyIdentityEndpointWebApplicationFactory : TestWebApplicationFactory<ExternalAuthenticationTestEntryPoint>
{
    protected override IHostBuilder CreateHostBuilder() => new HostBuilder();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        ExternalAuthenticationTestHost.Configure(
            builder,
            typeof(IdentityFeature).Assembly,
            endpoint => endpoint.Namespace is "Flamme.Identity.Endpoints.Login" or "Flamme.Identity.Endpoints.RefreshToken",
            IdentityAuthenticationSchemes.RefreshToken);
    }
}
