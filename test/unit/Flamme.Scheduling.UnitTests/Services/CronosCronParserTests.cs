using Microsoft.Extensions.Time.Testing;
using Flamme.Common;
using Flamme.Scheduling.Services;

namespace Flamme.Scheduling.UnitTests.Services;

public class CronosCronParserTests
{
    [Test]
    [Arguments("0 0 0 * * *")] // Daily at midnight
    [Arguments("0 0 */6 * * *")] // Every 6 hours
    [Arguments("0 0 0 * * MON")] // Every Monday
    [Arguments("0 0 9 * * MON-FRI")] // Weekdays at 9 AM
    [Arguments("0 0 0 1 */3 *")] // First day of every 3 months
    [Arguments("0 0 0 L * *")] // Last day of month
    public async Task GetNextOccurrence_WithValidExpression_ReturnsTimeInFuture(string cronExpression)
    {
        // Arrange
        var parser = CreateParser(out var now);

        // Act
        var nextOccurrence = parser.GetNextOccurrence(cronExpression);

        // Assert
        await Assert.That(nextOccurrence > now).IsTrue();
    }

    [Test]
    public async Task GetNextOccurrence_WithSecondsPrecision_ReturnsCorrectSecond()
    {
        // Arrange
        var parser = CreateParser(out _);

        // Act
        var nextOccurrence = parser.GetNextOccurrence("30 * * * * *");

        // Assert
        await Assert.That(nextOccurrence.Second).IsEqualTo(30);
    }

    [Test]
    public async Task GetNextOccurrence_AtMidnight_ReturnsNextDay()
    {
        // Arrange
        var now = new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero);
        var parser = CreateParser(now);

        // Act
        var nextOccurrence = parser.GetNextOccurrence("0 0 0 * * *");

        // Assert
        await Assert.That(nextOccurrence.Day).IsEqualTo(7);
    }

    [Test]
    public async Task GetNextOccurrence_EverySecond_ReturnsNextSecond()
    {
        // Arrange
        var now = new DateTimeOffset(2025, 1, 6, 12, 30, 45, TimeSpan.Zero);
        var parser = CreateParser(now);

        // Act
        var nextOccurrence = parser.GetNextOccurrence("* * * * * *");

        // Assert
        await Assert.That(nextOccurrence.Second).IsEqualTo(46);
        await Assert.That((nextOccurrence - now).TotalSeconds < 2).IsTrue();
    }

    [Test]
    public async Task GetNextOccurrence_WithSpecificDayOfMonth_ReturnsCorrectDay()
    {
        // Arrange
        var parser = CreateParser(out _);

        // Act
        var nextOccurrence = parser.GetNextOccurrence("0 0 0 15 * *");

        // Assert
        await Assert.That(nextOccurrence.Day).IsEqualTo(15);
        await Assert.That(nextOccurrence.Hour).IsEqualTo(0);
    }

    [Test]
    [Arguments("invalid")]
    [Arguments("* * * *")] // Too few fields
    [Arguments("60 * * * * *")] // Invalid second (>59)
    [Arguments("* 60 * * * *")] // Invalid minute (>59)
    [Arguments("* * 25 * * *")] // Invalid hour (>23)
    public async Task GetNextOccurrence_WithInvalidExpression_ThrowsException(string cronExpression)
    {
        // Arrange
        var parser = CreateParser(out _);

        // Act & Assert
        await Assert.That(() => parser.GetNextOccurrence(cronExpression)).Throws<Exception>();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(1)]
    public async Task GetNextOccurrence_PreservesUtcMinuteBoundaryWithANonUtcLocalZone(int offsetTicks)
    {
        var boundary = new DateTimeOffset(2026, 11, 15, 12, 1, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(boundary.AddTicks(offsetTicks));
        timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test-offset", TimeSpan.FromHours(3), "test-offset", "test-offset"));
        var parser = new CronosCronParser(timeProvider);

        var occurrence = parser.GetNextOccurrence("0 * * * * *");

        await Assert.That(occurrence).IsEqualTo(offsetTicks < 0 ? boundary : boundary.AddMinutes(1));
        await Assert.That(occurrence.Offset).IsEqualTo(TimeSpan.Zero);
    }

    private static CronosCronParser CreateParser(out DateTimeOffset now)
    {
        now = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero);
        return CreateParser(now);
    }

    private static CronosCronParser CreateParser(DateTimeOffset now)
    {
        var timeProvider = new FakeTimeProvider(now);
        return new CronosCronParser(timeProvider);
    }
}
