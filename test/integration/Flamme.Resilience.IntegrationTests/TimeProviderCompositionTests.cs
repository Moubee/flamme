using Flamme.Common.Features;
using Flamme.Extensions;
using Flamme.Features.Implementations;
using Flamme.Resilience.Endpoints.SimulateResponse;
using Flamme.Resilience.Extensions;
using Flamme.Resilience.Features;
using Flamme.Resilience.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Flamme.Resilience.IntegrationTests;

public class TimeProviderCompositionTests
{
    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task Features_ShareTheHostProviderInEitherOrder(bool resilienceFirst, bool registerBefore)
    {
        var services = new ServiceCollection();
        var fake = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddTicks(1_234_567));
        if (registerBefore) services.AddSingleton<TimeProvider>(fake);
        var module = new Module(services);
        if (resilienceFirst)
        {
            module.Configure<ResilienceFeature>();
            module.Configure<TimeProviderFeature>();
        }
        else
        {
            module.Configure<TimeProviderFeature>();
            module.Configure<ResilienceFeature>();
        }
        module.Apply();
        if (!registerBefore) services.AddSingleton<TimeProvider>(fake);
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await AssertSharedTimeAsync(container, fake);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AddFlammeAndResilience_PreserveTheHostProvider(bool registerBefore)
    {
        var services = new ServiceCollection();
        var fake = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddTicks(1_234_567));
        if (registerBefore) services.AddSingleton<TimeProvider>(fake);
        services.AddFlamme(module => module.UseResilience());
        if (!registerBefore) services.AddSingleton<TimeProvider>(fake);
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await AssertSharedTimeAsync(container, fake);
    }

    [Test]
    public async Task ResilienceStandalone_RegistersTheSystemProvider()
    {
        var services = new ServiceCollection();
        var module = new Module(services);
        module.Configure<ResilienceFeature>();
        module.Apply();
        using var container = services.BuildServiceProvider();

        await Assert.That(container.GetRequiredService<TimeProvider>()).IsSameReferenceAs(TimeProvider.System);
        await Assert.That(container.GetRequiredService<SimulateResponseSessionStore>().TryGetNextIndex("session", 3, out var index)).IsTrue();
        await Assert.That(index).IsEqualTo(0);
    }

    private static async Task AssertSharedTimeAsync(IServiceProvider container, FakeTimeProvider fake)
    {
        var provider = container.GetRequiredService<TimeProvider>();
        var sessions = container.GetRequiredService<SimulateResponseSessionStore>();
        await Assert.That(provider).IsSameReferenceAs(fake);
        await Assert.That(provider.GetUtcNow()).IsEqualTo(fake.GetUtcNow());
        await Assert.That(sessions.TryGetNextIndex("session", 3, out var firstIndex)).IsTrue();
        await Assert.That(firstIndex).IsEqualTo(0);

        fake.Advance(container.GetRequiredService<IOptions<SimulateResponseOptions>>().Value.SessionSlidingExpiration);

        await Assert.That(provider.GetUtcNow()).IsEqualTo(fake.GetUtcNow());
        await Assert.That(sessions.TryGetNextIndex("session", 3, out var expiredIndex)).IsTrue();
        await Assert.That(expiredIndex).IsEqualTo(0);
    }
}
