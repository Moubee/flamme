using Flamme.Common.Features;
using Flamme.Features.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Flamme.Common.UnitTests;

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
        await Assert.That(container.GetRequiredService<TimeProvider>()).IsSameReferenceAs(provider);
        await Assert.That(provider.GetUtcNow()).IsEqualTo(instant);
        fake.Advance(TimeSpan.FromTicks(37));
        await Assert.That(provider.GetUtcNow()).IsEqualTo(instant.AddTicks(37));
    }

    [Test]
    public async Task SingletonConsumer_RejectsAScopedProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<TimeProvider>(_ => new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        services.AddSingleton<SingletonConsumer>();
        var module = new Module(services);
        module.Configure<TimeProviderFeature>();
        module.Apply();

        await Assert.That(() => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        })).Throws<AggregateException>();
    }

    public sealed class SingletonConsumer(TimeProvider timeProvider)
    {
        public TimeProvider TimeProvider { get; } = timeProvider;
    }
}
