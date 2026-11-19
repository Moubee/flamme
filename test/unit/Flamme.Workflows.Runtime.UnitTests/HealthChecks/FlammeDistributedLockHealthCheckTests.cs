using Flamme.Workflows.Runtime.HealthChecks;
using Flamme.Workflows.Runtime.Options;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.HealthChecks;

public class FlammeDistributedLockHealthCheckTests : IAsyncDisposable
{
    private static readonly TimeSpan ExpectedLockAcquisitionTimeout = TimeSpan.FromMilliseconds(250);
    private readonly IDistributedLockProvider _distributedLockProvider = IDistributedLockProvider.Mock().Object;
    private readonly IDistributedLock _distributedLock = IDistributedLock.Mock().Object;
    private readonly ServiceProvider _serviceProvider;
    private readonly FlammeDistributedLockHealthCheck _sut;

    public FlammeDistributedLockHealthCheckTests()
    {
        Mock.Get(_distributedLockProvider).CreateLock(Arg.Any<string>()).Returns(_distributedLock);
        Mock.Get(_distributedLock).TryAcquireAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                    .Returns(IDistributedSynchronizationHandle.Mock().Object);
        _serviceProvider = CreateServiceProvider(_distributedLockProvider);
        _sut = new FlammeDistributedLockHealthCheck(_serviceProvider, CreateOptions(), NullLogger<FlammeDistributedLockHealthCheck>.Instance);
    }

    [Test]
    public async Task ReturnsHealthyWhenProbeLockCanBeAcquired()
    {
        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["category"]).IsEqualTo("distributed-locks");
        Mock.Get(_distributedLockProvider).CreateLock(Arg.Is<string>(x => IsProbeLockName(x))).WasCalled(Times.Exactly(1));
        Mock.Get(_distributedLock).TryAcquireAsync(ExpectedLockAcquisitionTimeout, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task UsesUniqueProbeLockNameForEachCheck()
    {
        var lockNames = new List<string>();
        var distributedLockProvider = IDistributedLockProvider.Mock().Object;
        var distributedLock = IDistributedLock.Mock().Object;
        Mock.Get(distributedLockProvider).CreateLock(Arg.Any<string>()).Callback((string name) => lockNames.Add(name)).Returns(distributedLock);
        Mock.Get(distributedLock).TryAcquireAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                    .Returns(IDistributedSynchronizationHandle.Mock().Object);
        await using var serviceProvider = CreateServiceProvider(distributedLockProvider);
        var sut = new FlammeDistributedLockHealthCheck(serviceProvider, CreateOptions(), NullLogger<FlammeDistributedLockHealthCheck>.Instance);

        await sut.CheckHealthAsync(new HealthCheckContext());
        await sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(lockNames.Count).IsEqualTo(2);
        foreach (var lockName in lockNames)
            await Assert.That(lockName).DoesNotContain(Environment.MachineName).WithComparison(StringComparison.OrdinalIgnoreCase);
        await Assert.That(lockNames[1]).IsNotEqualTo(lockNames[0]);
        foreach (var lockName in lockNames)
            await Assert.That(IsProbeLockName(lockName)).IsTrue();
    }

    [Test]
    public async Task ReturnsDegradedWhenProbeLockCannotBeAcquired()
    {
        Mock.Get(_distributedLock).TryAcquireAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                    .Returns((IDistributedSynchronizationHandle?)null);

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["category"]).IsEqualTo("distributed-locks");
    }

    [Test]
    public async Task ReturnsUnhealthyWhenProviderThrows()
    {
        Mock.Get(_distributedLockProvider).CreateLock(Arg.Any<string>()).Returns(() => throw new InvalidOperationException("lock backend unavailable"));

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["category"]).IsEqualTo("distributed-locks");
    }

    [Test]
    public async Task ReturnsDegradedWhenProviderIsNotRegistered()
    {
        await using var serviceProvider = CreateServiceProvider();
        var sut = new FlammeDistributedLockHealthCheck(serviceProvider, CreateOptions(), NullLogger<FlammeDistributedLockHealthCheck>.Instance);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["category"]).IsEqualTo("distributed-locks");
    }

    public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

    private static ServiceProvider CreateServiceProvider(IDistributedLockProvider? distributedLockProvider = null)
    {
        var services = new ServiceCollection();

        if (distributedLockProvider != null)
            services.AddSingleton(distributedLockProvider);

        return services.BuildServiceProvider();
    }

    private static IOptions<FlammeReadinessHealthCheckOptions> CreateOptions() => Microsoft.Extensions.Options.Options.Create(new FlammeReadinessHealthCheckOptions
    {
        DistributedLockAcquisitionTimeout = ExpectedLockAcquisitionTimeout
    });

    private static bool IsProbeLockName(string lockName)
    {
        const string prefix = "elsa-health-check-";
        return lockName.StartsWith(prefix, StringComparison.Ordinal)
               && Guid.TryParseExact(lockName[prefix.Length..], "N", out _);
    }
}
