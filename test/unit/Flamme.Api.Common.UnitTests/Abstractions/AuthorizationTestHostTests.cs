using Flamme.Authorization;
using Flamme.Testing.Shared.Authorization;

namespace Flamme.Api.Common.UnitTests.Abstractions;

[NotInParallel(nameof(EndpointSecurityCollection))]
public class AuthorizationTestHostTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Disposing_RestoresTheSecuritySettingFromBeforeTheHostStarted(bool securityWasEnabled)
    {
        var original = EndpointSecurityOptions.SecurityIsEnabled;
        EndpointSecurityOptions.SecurityIsEnabled = securityWasEnabled;

        try
        {
            var host = await AuthorizationTestHost.StartAsync<RequireAnyPermissionTests.AnyOfEndpoint>(endpoint => endpoint == typeof(RequireAnyPermissionTests.AnyOfEndpoint));
            await Assert.That(EndpointSecurityOptions.SecurityIsEnabled).IsTrue();

            await host.DisposeAsync();

            await Assert.That(EndpointSecurityOptions.SecurityIsEnabled).IsEqualTo(securityWasEnabled);
        }
        finally
        {
            EndpointSecurityOptions.SecurityIsEnabled = original;
        }
    }
}
