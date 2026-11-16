using Elsa.Common;
using Elsa.Mediator.Contracts;
using Elsa.Scheduling.ScheduledTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Scheduling.UnitTests.ScheduledTasks;

/// <summary>
/// Tests for ScheduledRecurringTask to ensure recurring tasks handle edge cases correctly.
/// </summary>
public class ScheduledRecurringTaskTests : IDisposable
{
    private static readonly DateTimeOffset DefaultNow = new(2025, 11, 06, 22, 50, 00, 0, TimeSpan.Zero);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    private readonly ServiceProvider _serviceProvider;
    private readonly RecordingTimeProvider _timeProvider;
    private readonly ILogger_TCategoryName_Mock<ScheduledRecurringTask> _logger;
    private readonly List<ScheduledRecurringTask> _tasksToDispose = new();

    public ScheduledRecurringTaskTests()
    {
        var services = new ServiceCollection();
        _timeProvider = new RecordingTimeProvider(DefaultNow);
        _logger = ILogger<ScheduledRecurringTask>.Mock();

        services.AddSingleton(ICommandSender.Mock().Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    private ScheduledRecurringTask CreateScheduledTask(
        DateTimeOffset? startAt = null,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null)
    {
        var task = ITask.Mock().Object;
        var scheduledTask = new ScheduledRecurringTask(
            task,
            startAt ?? DefaultNow.AddMinutes(5),
            interval ?? DefaultInterval,
            timeProvider ?? _timeProvider,
            _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger.Object
        );
        _tasksToDispose.Add(scheduledTask);
        return scheduledTask;
    }

    private void SetupTimeProvider(DateTimeOffset now)
    {
        _timeProvider.SetUtcNow(now);
    }

    private void AssertNoErrorLogged()
    {
        _logger.Log<AnyType>(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<AnyType>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<AnyType, Exception?, string>>()).WasNeverCalled();
    }

    private void AssertWarningLogged(int expectedCount = 1)
    {
        _logger.Log<AnyType>(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<AnyType>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<AnyType, Exception?, string>>()).WasCalled(Times.Exactly(expectedCount));
    }

    [Test]
    public void Schedule_WithVerySmallDelay_ShouldStillSetupTimer()
    {
        // Arrange - simulate a case where the delay is very small (1 tick = 100ns)
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow.AddTicks(1); // Only 1 tick in the future (100 nanoseconds)

        // Act
        CreateScheduledTask(startAt: startAt);

        // Assert - Verify that no error was logged (timer should be set up successfully)
        AssertNoErrorLogged();
    }

    [Test]
    public async Task Schedule_WithZeroDelay_ShouldRetryAndSetupTimer()
    {
        // Arrange - simulate a case where the first call returns exactly now
        // but the second call returns a proper future time
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow; // First: delay=0

        // Act
        var task = CreateScheduledTask(startAt: startAt);

        // Dispose immediately to prevent timer from firing
        ((IDisposable)task).Dispose();

        // Assert - System timeProvider should be called at least twice (initial + retry)
        // May be called more if timer fires before disposal in rare race conditions
        await Assert.That(_timeProvider.ReadCount >= 2).IsTrue();
    }

    [Test]
    public async Task Schedule_WithNegativeDelay_ShouldRetryAndSetupTimer()
    {
        // Arrange - simulate a case where the first call returns a time in the past
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow.AddMinutes(-1); // Past time

        // Act
        var task = CreateScheduledTask(startAt: startAt);

        // Dispose immediately to prevent timer from firing
        ((IDisposable)task).Dispose();

        // Assert - System timeProvider should be called at least twice (initial + retry)
        // May be called more if timer fires before disposal in rare race conditions
        await Assert.That(_timeProvider.ReadCount >= 2).IsTrue();
    }

    [Test]
    public async Task Schedule_WithPersistentZeroDelay_ShouldLogWarningAndUseMinimumDelay()
    {
        // Arrange - simulate the bug scenario: both attempts return zero/negative delay
        // This can happen if the system timeProvider doesn't advance or if there's timeProvider drift
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow; // Both calls return exactly now (delay = 0)

        // Act - This should not crash and should set up a timer with minimum delay
        var task = CreateScheduledTask(startAt: startAt);

        // Dispose immediately to prevent timer from firing and recursing
        ((IDisposable)task).Dispose();
        Thread.Sleep(5); // Brief wait to ensure disposal completes

        // Assert - Should call UtcNow at least twice (initial + retry)
        // May be called more if timer fires before disposal
        await Assert.That(_timeProvider.ReadCount >= 2).IsTrue();
        AssertWarningLogged();
    }

    [Test]
    public void Schedule_WithNegativeDelayAfterRetry_ShouldLogWarningAndUseMinimumDelay()
    {
        // Arrange - simulate a case where even after retry, delay is negative
        // This could happen due to system timeProvider adjustments
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow.AddMilliseconds(-100); // Negative delay

        // Act - Should handle negative delay gracefully
        var task = CreateScheduledTask(startAt: startAt);

        // Dispose immediately to prevent timer from firing
        ((IDisposable)task).Dispose();

        // Assert - Should log a warning and still set up timer
        AssertWarningLogged();
    }

    [Test]
    public void DisposeDuringTimerCallback_ShouldNotCrash()
    {
        // Arrange - set up a very short delay so timer fires quickly
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow; // Will use 1ms minimum delay

        // Act - Create task and immediately dispose it (simulating race condition)
        var task = CreateScheduledTask(startAt: startAt);
        Thread.Sleep(5); // Give timer a chance to start firing
        ((IDisposable)task).Dispose();
        Thread.Sleep(10); // Give any in-flight callbacks time to complete

        // Assert - Should not crash (implicit - test passes if no exception thrown)
    }

    public void Dispose()
    {
        // Dispose tasks first to stop timers before disposing ServiceProvider
        foreach (var task in _tasksToDispose)
        {
            ((IDisposable)task).Dispose();
        }
        _serviceProvider.Dispose();
    }

    private sealed class RecordingTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private readonly FakeTimeProvider _time = new(initialUtc);
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public void SetUtcNow(DateTimeOffset value) => _time.SetUtcNow(value);

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref _readCount);
            return _time.GetUtcNow();
        }
    }
}
