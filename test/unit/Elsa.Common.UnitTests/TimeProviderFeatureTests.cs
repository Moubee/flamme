using Elsa.Common.Features;
using Elsa.Features.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Common.UnitTests;

public class TimeProviderFeatureTests
{
    [Test]
    public async Task DefaultProvider_IsSystemProvider()
    {
        var services = new ServiceCollection();
        var module = new Module(services);
        module.Configure<TimeProviderFeature>();
        module.Apply();
        using var container = services.BuildServiceProvider();
        await Assert.That(container.GetRequiredService<TimeProvider>()).IsSameReferenceAs(TimeProvider.System);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task HostProvider_WinsBeforeOrAfterFeatureApplication(bool registerBefore)
    {
        var services = new ServiceCollection();
        var instant = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var fake = new FakeTimeProvider(instant);
        if (registerBefore) services.AddSingleton<TimeProvider>(fake);
        var module = new Module(services);
        module.Configure<TimeProviderFeature>();
        module.Apply();
        new TimeProviderFeature(module).Apply();
        if (!registerBefore) services.AddSingleton<TimeProvider>(fake);
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = container.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await Assert.That(provider).IsSameReferenceAs(fake);
        await Assert.That(provider.GetUtcNow()).IsEqualTo(instant);
        fake.Advance(TimeSpan.FromTicks(37));
        await Assert.That(provider.GetUtcNow()).IsEqualTo(instant.AddTicks(37));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LegacyFacade_UsesTheHostProviderAcrossScopes(bool registerBefore)
    {
        var services = new ServiceCollection();
        var initialUtc = DateTimeOffset.UnixEpoch.AddTicks(1_234_567);
        var fake = new FakeTimeProvider(initialUtc);
        if (registerBefore) services.AddSingleton<TimeProvider>(fake);
        var module = new Module(services);
        module.Configure<SystemClockFeature>();
        module.Apply();
        if (!registerBefore) services.AddSingleton<TimeProvider>(fake);
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = container.CreateScope();
        var facade = scope.ServiceProvider.GetRequiredService<ISystemClock>();

        await Assert.That(container.GetRequiredService<ISystemClock>()).IsSameReferenceAs(facade);
        await Assert.That(scope.ServiceProvider.GetRequiredService<TimeProvider>()).IsSameReferenceAs(fake);
        await Assert.That(facade.UtcNow).IsEqualTo(initialUtc);
        fake.Advance(TimeSpan.FromTicks(37));
        await Assert.That(facade.UtcNow).IsEqualTo(initialUtc.AddTicks(37));
    }

    [Test]
    public async Task LegacyFacade_RejectsAScopedProviderWhenResolved()
    {
        var services = new ServiceCollection();
        services.AddScoped<TimeProvider>(_ => new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        var module = new Module(services);
        module.Configure<SystemClockFeature>();
        module.Apply();
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Factory dependencies are checked on resolution, rather than by ValidateOnBuild.
        await Assert.That(() => container.GetRequiredService<ISystemClock>()).Throws<InvalidOperationException>();
    }

}
