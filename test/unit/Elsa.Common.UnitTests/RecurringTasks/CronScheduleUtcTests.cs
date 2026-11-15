using Cronos;
using Elsa.Common.RecurringTasks;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Common.UnitTests.RecurringTasks;

public class CronScheduleUtcTests
{
    [Test]
    public async Task Cronos_RejectsTheUnspecifiedKindProducedByDateTimeOffsetDateTime()
    {
        var instant = new DateTimeOffset(2026, 11, 15, 12, 0, 15, TimeSpan.Zero);
        var expression = CronExpression.Parse("* * * * *");

        await Assert.That(instant.DateTime.Kind).IsEqualTo(DateTimeKind.Unspecified);
        await Assert.That(() => expression.GetNextOccurrence(instant.DateTime)).Throws<ArgumentException>();
        await Assert.That(expression.GetNextOccurrence(instant.UtcDateTime)).IsEqualTo(instant.AddSeconds(45).UtcDateTime);
    }

    [Test]
    public async Task CreateTimer_UsesUtcKindAndPreservesBothClockReads()
    {
        var initialUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 15, TimeSpan.Zero).AddTicks(1234567);
        var time = new FakeTimeProvider(initialUtc) { AutoAdvanceAmount = TimeSpan.FromTicks(1) };
        var schedule = new CronSchedule(time, CronExpression.Parse("* * * * *"));

        await using var timer = schedule.CreateTimer(() => Task.CompletedTask);

        await Assert.That(time.GetUtcNow()).IsEqualTo(initialUtc.AddTicks(2));
    }
}
