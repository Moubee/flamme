using Flamme.Common;
using Flamme.Mediator.Contracts;
using Flamme.Scheduling.Options;
using Flamme.Scheduling.ScheduledTasks;
using Flamme.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Flamme.Scheduling.UnitTests.ScheduledTasks;

/// <summary>
/// Tests for ScheduledSpecificInstantTask to ensure specific instant tasks handle edge cases correctly.
/// </summary>
public class ScheduledSpecificInstantTaskTests : IDisposable
{
    private static readonly DateTimeOffset DefaultNow = new(2025, 11, 06, 22, 50, 00, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ILogger_TCategoryName_Mock<ScheduledSpecificInstantTask> _logger;
    private readonly PastDueScheduleStaggerer _pastDueScheduleStaggerer = new(OptionsFactory.Create(new SchedulingOptions
    {
        MinimumPastDueScheduleDelay = TimeSpan.FromMilliseconds(10),
        PastDueScheduleStaggerInterval = TimeSpan.FromMilliseconds(25),
        PastDueScheduleStaggerWindow = TimeSpan.FromMilliseconds(100)
    }));
    private readonly List<ScheduledSpecificInstantTask> _tasksToDispose = new();

    public ScheduledSpecificInstantTaskTests()
    {
        var services = new ServiceCollection();
        _timeProvider = new FakeTimeProvider(DefaultNow);
        _logger = ILogger<ScheduledSpecificInstantTask>.Mock();

        services.AddSingleton(ICommandSender.Mock().Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    private ScheduledSpecificInstantTask CreateScheduledTask(
        DateTimeOffset? startAt = null,
        TimeProvider? timeProvider = null)
    {
        var task = ITask.Mock().Object;
        var scheduledTask = new ScheduledSpecificInstantTask(
            task,
            startAt ?? DefaultNow.AddMinutes(5),
            timeProvider ?? _timeProvider,
            _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger.Object,
            _pastDueScheduleStaggerer
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

    private void AssertDebugLogged(int expectedCount = 1)
    {
        _logger.Log<AnyType>(
            LogLevel.Debug,
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
    public void Schedule_WithZeroDelay_ShouldUseCatchUpDelay()
    {
        // Arrange - simulate a case where startAt is exactly now
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow; // delay = 0

        // Act - Should adjust to a bounded catch-up delay
        CreateScheduledTask(startAt: startAt);

        AssertDebugLogged();
    }

    [Test]
    public void Schedule_WithNegativeDelay_ShouldUseCatchUpDelay()
    {
        // Arrange - simulate a case where startAt is in the past
        SetupTimeProvider(DefaultNow);
        var startAt = DefaultNow.AddMinutes(-1); // Past time

        // Act - Should adjust to a bounded catch-up delay
        CreateScheduledTask(startAt: startAt);

        AssertDebugLogged();
    }

    [Test]
    public async Task Schedule_WithMultiplePastDueTasks_ShouldStaggerCatchUp()
    {
        SetupTimeProvider(DefaultNow);

        CreateScheduledTask(startAt: DefaultNow.AddMinutes(-1));
        CreateScheduledTask(startAt: DefaultNow.AddMinutes(-1));
        CreateScheduledTask(startAt: DefaultNow.AddMinutes(-1));

        var debugMessages = _logger.Invocations
            .Where(call => call.MemberName == nameof(ILogger.Log) && call.Arguments[0] is LogLevel.Debug)
            .Select(call => call.Arguments[2]?.ToString())
            .ToArray();
        await Assert.That(debugMessages).HasSingleItem(message => message?.Contains("00:00:00.0100000") == true);
        await Assert.That(debugMessages).HasSingleItem(message => message?.Contains("00:00:00.0350000") == true);
        await Assert.That(debugMessages).HasSingleItem(message => message?.Contains("00:00:00.0600000") == true);
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
        // Small delay to ensure any timer callbacks have completed
        Thread.Sleep(10);
        _serviceProvider.Dispose();
    }
}
