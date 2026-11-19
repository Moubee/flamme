using System.Reflection;
using Flamme.ExternalAuthentication.IntegrationTests.Fixtures;
using Flamme.Identity.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace Flamme.ExternalAuthentication.IntegrationTests.Identity;

public sealed class RoleDeletionEndpointContractWebApplicationFactory : TestWebApplicationFactory<ExternalAuthenticationTestEntryPoint>
{
    protected override IHostBuilder CreateHostBuilder() => new HostBuilder();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        ExternalAuthenticationTestHost.Configure(builder, typeof(IdentityFeature).Assembly, endpoint => endpoint.Namespace == "Flamme.Identity.Endpoints.Roles.Delete");
    }
}
