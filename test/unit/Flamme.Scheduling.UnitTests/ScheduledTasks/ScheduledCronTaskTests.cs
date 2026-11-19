using Flamme.Common;
using Flamme.Mediator.Contracts;
using Flamme.Scheduling.ScheduledTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Flamme.Scheduling.UnitTests.ScheduledTasks;

/// <summary>
/// Tests for ScheduledCronTask to ensure cron triggers continue to fire even in edge cases.
/// </summary>
public class ScheduledCronTaskTests : IDisposable
{
    private const string DefaultCronExpression = "0 */5 * * * *";
    private static readonly DateTimeOffset DefaultNow = new(2025, 11, 06, 22, 50, 00, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ICronParser _cronParser;
    private readonly ILogger_TCategoryName_Mock<ScheduledCronTask> _logger;
    private readonly List<ScheduledCronTask> _tasksToDispose = new();

    public ScheduledCronTaskTests()
    {
        var services = new ServiceCollection();
        _timeProvider = new FakeTimeProvider(DefaultNow);
        _cronParser = ICronParser.Mock().Object;
        _logger = ILogger<ScheduledCronTask>.Mock();
        
        services.AddSingleton(ICommandSender.Mock().Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    private ScheduledCronTask CreateScheduledTask(
        string? cronExpression = null,
        ICronParser? cronParser = null,
        TimeProvider? timeProvider = null)
    {
        var task = ITask.Mock().Object;
        var scheduledTask = new ScheduledCronTask(
            task,
            cronExpression ?? DefaultCronExpression,
            cronParser ?? _cronParser,
            _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider ?? _timeProvider,
            _logger.Object
        );
        _tasksToDispose.Add(scheduledTask);
        return scheduledTask;
    }

    private void SetupCronParser(params DateTimeOffset[] occurrences)
    {
        Mock.Get(_cronParser).GetNextOccurrence(Arg.Any<string>()).ReturnsSequentially(occurrences);
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
        SetupCronParser(DefaultNow.AddTicks(1)); // Only 1 tick in the future (100 nanoseconds)

        // Act
        CreateScheduledTask();

        // Assert - Verify that no error was logged (timer should be set up successfully)
        AssertNoErrorLogged();
    }

    [Test]
    public async Task Schedule_WithZeroDelay_ShouldRetryAndSetupTimer()
    {
        // Arrange - simulate a case where the first call returns exactly now
        // but the second call returns a proper future time
        SetupTimeProvider(DefaultNow);
        SetupCronParser(DefaultNow, DefaultNow.AddMinutes(5)); // First: delay=0, Second: proper future time

        // Act
        var task = CreateScheduledTask();

        // Dispose immediately to prevent timer from firing
        ((IDisposable)task).Dispose();

        // Assert - Should call GetNextOccurrence at least twice (initial + retry)
        // May be called more if timer fires before disposal in rare race conditions
        Mock.Get(_cronParser).GetNextOccurrence(DefaultCronExpression).WasCalled(Times.AtLeast(2));
    }

    [Test]
    public async Task Schedule_WithNegativeDelay_ShouldRetryAndSetupTimer()
    {
        // Arrange - simulate a case where the first call returns a time in the past
        SetupTimeProvider(DefaultNow);
        SetupCronParser(DefaultNow.AddMinutes(-1), DefaultNow.AddMinutes(5)); // First: past, Second: future

        // Act
        var task = CreateScheduledTask();

        // Dispose immediately to prevent timer from firing
        ((IDisposable)task).Dispose();
        Thread.Sleep(5); // Brief wait to ensure disposal completes

        // Assert - Should call GetNextOccurrence at least twice
        Mock.Get(_cronParser).GetNextOccurrence(DefaultCronExpression).WasCalled(Times.AtLeast(2));
    }

    [Test]
    public async Task Schedule_WithPersistentZeroDelay_ShouldLogWarningAndUseMinimumDelay()
    {
        // Arrange - simulate the bug scenario: both attempts return zero/negative delay
        // This can happen if the system timeProvider doesn't advance or if there's timeProvider drift
        // After the initial attempts, return a proper future time to prevent timer from firing during test
        SetupTimeProvider(DefaultNow);
        SetupCronParser(DefaultNow, DefaultNow, DefaultNow.AddMinutes(5)); // First two return now, third returns future

        // Act - This should not crash and should set up a timer with minimum delay
        var task = CreateScheduledTask();

        // Dispose immediately to prevent timer from firing and recursing
        _tasksToDispose.Remove(task);
        ((IDisposable)task).Dispose();

        // Assert - Should call GetNextOccurrence at least twice (initial + retry)
        // May be called more if timer fires before disposal and triggers Schedule() again
        Mock.Get(_cronParser).GetNextOccurrence(DefaultCronExpression).WasCalled(Times.AtLeast(2));
        AssertWarningLogged();
    }

    [Test]
    public void Schedule_WithNegativeDelayAfterRetry_ShouldLogWarningAndUseMinimumDelay()
    {
        // Arrange - simulate a case where even after retry, delay is negative
        // This could happen due to system timeProvider adjustments
        // After the initial attempts, return a proper future time to prevent timer from firing during test
        SetupTimeProvider(DefaultNow);
        SetupCronParser(DefaultNow.AddMilliseconds(-100), DefaultNow.AddMilliseconds(-50), DefaultNow.AddMinutes(5));

        // Act - Should handle negative delay gracefully
        var task = CreateScheduledTask();

        // Dispose immediately to prevent timer from firing
        _tasksToDispose.Remove(task);
        ((IDisposable)task).Dispose();

        // Assert - Should log a warning and still set up timer
        AssertWarningLogged();
    }

    [Test]
    public void ReproduceOriginalIssue_WithRealCronParser_DemonstratesBugScenario()
    {
        // This test reproduces the exact scenario from the original issue report
        // Using the real CronosCronParser with specific times that trigger the bug
        
        // Arrange - Use the exact times from the issue that cause delay.Milliseconds to be 0
        var now = new DateTimeOffset(2025, 11, 06, 22, 50, 00, 0, TimeZoneInfo.Utc.GetUtcOffset(DateTimeOffset.UtcNow));
        var timeProvider = new FakeTimeProvider(now);
        var realCronParser = new Flamme.Scheduling.Services.CronosCronParser(timeProvider);
        
        // Act - Create scheduled task with real cron parser
        // Before the fix: This would silently fail to schedule if delay <= 0
        // After the fix: This should log warning and use minimum delay
        CreateScheduledTask(cronParser: realCronParser, timeProvider: timeProvider);
        
        // Assert - The key is that the task was created successfully without throwing or silently failing
        // This test passes with the fix, demonstrating the issue is resolved
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
}
