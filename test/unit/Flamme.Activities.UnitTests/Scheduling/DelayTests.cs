using Flamme.Extensions;
using Flamme.Scheduling;
using Flamme.Scheduling.Activities;
using Flamme.Scheduling.Bookmarks;
using Flamme.Testing.Shared;
using Flamme.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;

namespace Flamme.Activities.UnitTests.Scheduling;

public class DelayTests
{
    [Test]
    [Arguments(1, 0, 0)]    // 1 hour
    [Arguments(0, 30, 0)]   // 30 minutes
    [Arguments(0, 0, 45)]   // 45 seconds
    [Arguments(24, 0, 0)]   // 1 day
    public async Task CreatesBookmark_WithCorrectResumeTime(int hours, int minutes, int seconds)
    {
        // Arrange
        var now = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero);
        var delay = new TimeSpan(hours, minutes, seconds);
        var expectedResumeAt = now.Add(delay);
        var clock = CreateClock(now);

        var activity = new Delay(delay);

        // Act
        var context = await ExecuteAsync(activity, clock);

        // Assert
        await Assert.That(context.Status).IsEqualTo(ActivityStatus.Running);
        var payload = await GetDelayPayload(context);
        await Assert.That(payload.ResumeAt).IsEqualTo(expectedResumeAt);
    }

    [Test]
    public async Task FixtureClockAdvancement_IsIsolatedAndControlsBookmarkTime()
    {
        var duration = TimeSpan.FromMinutes(1);
        await using var firstFixture = new ActivityTestFixture(new Delay(duration));
        await using var secondFixture = new ActivityTestFixture(new Delay(duration));
        var firstContext = await firstFixture.BuildAsync();
        var secondContext = await secondFixture.BuildAsync();
        var firstClock = (FakeTimeProvider)firstContext.ExpressionExecutionContext.GetRequiredService<TimeProvider>();
        var secondClock = (FakeTimeProvider)secondContext.ExpressionExecutionContext.GetRequiredService<TimeProvider>();
        var initialUtc = secondClock.GetUtcNow();

        firstClock.Advance(TimeSpan.FromMinutes(5));
        await firstFixture.ExecuteAsync(firstContext);
        await secondFixture.ExecuteAsync(secondContext);

        var firstPayload = await GetDelayPayload(firstContext);
        var secondPayload = await GetDelayPayload(secondContext);
        await Assert.That(firstPayload.ResumeAt).IsEqualTo(initialUtc.AddMinutes(5).Add(duration));
        await Assert.That(secondPayload.ResumeAt).IsEqualTo(initialUtc.Add(duration));
        await Assert.That(secondClock.GetUtcNow()).IsEqualTo(initialUtc);
    }

    [Test]
    [MethodDataSource(nameof(FactoryMethodTestCases))]
    public async Task FactoryMethods_CreateCorrectTimeSpans(Delay activity, TimeSpan expected)
    {
        // Arrange
        var now = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero);
        var clock = CreateClock(now);

        // Act
        var context = await ExecuteAsync(activity, clock);

        // Assert
        var payload = await GetDelayPayload(context);
        await Assert.That(payload.ResumeAt).IsEqualTo(now.Add(expected));
    }

    public static IEnumerable<Func<(Delay, TimeSpan)>> FactoryMethodTestCases() =>
    [
        () => (Delay.FromSeconds(30), TimeSpan.FromSeconds(30)),
        () => (Delay.FromMinutes(5), TimeSpan.FromMinutes(5)),
        () => (Delay.FromHours(2), TimeSpan.FromHours(2)),
        () => (Delay.FromDays(1), TimeSpan.FromDays(1))
    ];

    private static async Task<ActivityExecutionContext> ExecuteAsync(Delay activity, TimeProvider clock)
    {
        return await new ActivityTestFixture(activity)
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(clock))
            .ExecuteAsync();
    }

    private static TimeProvider CreateClock(DateTimeOffset now)
    {
        return new FakeTimeProvider(now);
    }

    private static async Task<DelayPayload> GetDelayPayload(ActivityExecutionContext context)
    {
        var bookmark = await Assert.That(context.WorkflowExecutionContext.Bookmarks).HasSingleItem();
        await Assert.That(bookmark.Name).IsEqualTo(SchedulingStimulusNames.Delay);
        await Assert.That(bookmark.Payload).IsOfType(typeof(DelayPayload));
        return (DelayPayload)bookmark.Payload!;
    }
}
