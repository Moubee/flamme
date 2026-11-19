
namespace Elsa.Resilience.Core.UnitTests.TestHelpers;

internal static class TestDataFactory
{
    public static IResilienceStrategy CreateStrategy(string id, string displayName)
    {
        var strategy = IResilienceStrategy.Mock().Object;
        Mock.Get(strategy).Id.Returns(id);
        Mock.Get(strategy).DisplayName.Returns(displayName);
        return strategy;
    }

    public static IResilienceStrategySource CreateStrategySource(params IResilienceStrategy[] strategies)
    {
        var source = IResilienceStrategySource.Mock().Object;
        Mock.Get(source).GetStrategiesAsync(Arg.Any<CancellationToken>()).Returns(strategies);
        return source;
    }
}
