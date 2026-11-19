using Microsoft.Extensions.Time.Testing;
using Elsa.Dashboard.Abstractions.Models;
using Elsa.Dashboard.Api.Services;
using System.Threading.Tasks;

namespace Elsa.Dashboard.Api.UnitTests;

public class DashboardRangeResolverTests
{
    private readonly DateTimeOffset _now = new(2026, 06, 01, 12, 00, 00, TimeSpan.Zero);
    private readonly DashboardRangeResolver _resolver;

    public DashboardRangeResolverTests()
    {
        _resolver = new(new FakeTimeProvider(_now));
    }

    [Test]
    [Arguments("1h", DashboardRangeKeys.OneHour, 1)]
    [Arguments("24h", DashboardRangeKeys.TwentyFourHours, 24)]
    [Arguments("7d", DashboardRangeKeys.SevenDays, 168)]
    [Arguments(null, DashboardRangeKeys.TwentyFourHours, 24)]
    [Arguments("unknown", DashboardRangeKeys.TwentyFourHours, 24)]
    public async Task Resolve_ReturnsExpectedRange(string? input, string expectedKey, int expectedHours)
    {
        var range = _resolver.Resolve(input);

        await Assert.That(range.Key).IsEqualTo(expectedKey);
        await Assert.That(range.To).IsEqualTo(_now);
        await Assert.That(range.From).IsEqualTo(_now.AddHours(-expectedHours));
    }

    [Test]
    public async Task Resolve_UsesCurrentInstantAndPreservesFractionalTicks()
    {
        var initialUtc = _now.AddTicks(1_234_567);
        var fakeTime = new FakeTimeProvider(initialUtc);
        var resolver = new DashboardRangeResolver(fakeTime);

        fakeTime.Advance(TimeSpan.FromMinutes(5));
        var range = resolver.Resolve(DashboardRangeKeys.OneHour);
        var expectedTo = initialUtc.AddMinutes(5);

        await Assert.That(range.To).IsEqualTo(expectedTo);
        await Assert.That(range.From).IsEqualTo(expectedTo.AddHours(-1));
    }

    [Test]
    [Arguments(DashboardRangeKeys.OneHour, DashboardTrendGranularity.Minute)]
    [Arguments(DashboardRangeKeys.TwentyFourHours, DashboardTrendGranularity.Hour)]
    [Arguments(DashboardRangeKeys.SevenDays, DashboardTrendGranularity.Day)]
    public async Task ResolveGranularity_ChoosesDefaultForRange(string range, string expectedGranularity)
    {
        var granularity = _resolver.ResolveGranularity(null, range);

        await Assert.That(granularity).IsEqualTo(expectedGranularity);
    }

    [Test]
    public async Task ResolveGranularity_PreservesExplicitGranularity()
    {
        var granularity = _resolver.ResolveGranularity("custom", DashboardRangeKeys.OneHour);

        await Assert.That(granularity).IsEqualTo("custom");
    }
}
