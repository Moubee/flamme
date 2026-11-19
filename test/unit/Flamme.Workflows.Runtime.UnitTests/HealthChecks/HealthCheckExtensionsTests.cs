using Flamme.Extensions;
using Flamme.Workflows.Runtime.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Flamme.Workflows.Runtime.UnitTests.HealthChecks;

public class HealthCheckExtensionsTests
{
    [Test]
    public async Task AddFlammeReadinessChecksRegistersReadinessOptions()
    {
        var services = new ServiceCollection();

        services
            .AddHealthChecks()
            .AddFlammeReadinessChecks(includePersistence: false, includeDistributedLocks: true);

        using var serviceProvider = services.BuildServiceProvider();

        await Assert.That(serviceProvider.GetRequiredService<IOptions<FlammeReadinessHealthCheckOptions>>()).IsNotNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void AddFlammeReadinessChecksRejectsNonPositiveDistributedLockTimeout(int timeoutMilliseconds)
    {
        var services = new ServiceCollection();

        services
            .AddHealthChecks()
            .AddFlammeReadinessChecks(
                includePersistence: false,
                includeDistributedLocks: true,
                configureOptions: options => options.DistributedLockAcquisitionTimeout = TimeSpan.FromMilliseconds(timeoutMilliseconds));

        using var serviceProvider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() => _ = serviceProvider.GetRequiredService<IOptions<FlammeReadinessHealthCheckOptions>>().Value);
    }

    [Test]
    public async Task AddFlammeReadinessChecksUsesFlammeSpecificReadinessTag()
    {
        var services = new ServiceCollection();

        services
            .AddHealthChecks()
            .AddFlammeReadinessChecks(includePersistence: false);

        using var serviceProvider = services.BuildServiceProvider();
        var registrations = serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var registration = await Assert.That(registrations).HasSingleItem();
        await Assert.That(registration.Tags).Contains("elsa");
        await Assert.That(registration.Tags).Contains(HealthCheckExtensions.ReadinessTag);
        await Assert.That(registration.Tags).DoesNotContain("readiness");
    }
}
