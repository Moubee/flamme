using Flamme.Alterations.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Flamme.Alterations.Core.UnitTests.Models;

public class AlterationLogTimeTests
{
    [Test]
    public async Task Add_PreservesTheCurrentInstantAndExistingEntries()
    {
        var initialUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var time = new FakeTimeProvider(initialUtc);
        var log = new AlterationLog(time);

        log.Add("started");
        time.Advance(TimeSpan.FromTicks(37));
        log.Add("completed", LogLevel.Warning, "completion");
        var entries = log.LogEntries.ToArray();

        await Assert.That(entries[0].Timestamp).IsEqualTo(initialUtc);
        await Assert.That(entries[1].Timestamp).IsEqualTo(initialUtc.AddTicks(37));
        await Assert.That(entries[1].EventName).IsEqualTo("completion");
        await Assert.That(entries[1].LogLevel).IsEqualTo(LogLevel.Warning);
    }
}
