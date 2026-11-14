
namespace Elsa.Resilience.Core.UnitTests.TestHelpers;

internal static class TestDataFactory
{
    public static IResilienceStrategy CreateStrategy(string id, string displayName)
    {
        var strategy = Mock.Of<IResilienceStrategy>().Object;
        Mock.Get(strategy).Id.Returns(id);
        Mock.Get(strategy).DisplayName.Returns(displayName);
        return strategy;
    }

    public static IResilienceStrategySource CreateStrategySource(params IResilienceStrategy[] strategies)
    {
        var source = Mock.Of<IResilienceStrategySource>().Object;
        Mock.Get(source).GetStrategiesAsync(Arg.Any<CancellationToken>()).Returns(strategies);
        return source;
    }
}
