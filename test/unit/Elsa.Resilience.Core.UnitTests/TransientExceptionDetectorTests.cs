using System.Threading.Tasks;

namespace Elsa.Resilience.Core.UnitTests;

public class TransientExceptionDetectorTests
{
    [Test]
    [DisplayName("Service with no registered strategies should return false for any exception")]
    public async Task IsTransient_NoDetectors_ReturnsFalse()
    {
        var detector = CreateDetector();
        var exception = new Exception("test");

        await Assert.That(detector.IsTransient(exception)).IsFalse();
    }

    [Test]
    [DisplayName("Service should return true when any strategy detects the exception as transient")]
    public async Task IsTransient_DetectorReturnsTrue_ReturnsTrue()
    {
        var exception = new Exception("test");
        var strategy = CreateStrategy((exception, true));
        var detector = CreateDetector(strategy);

        await Assert.That(detector.IsTransient(exception)).IsTrue();
    }

    [Test]
    [DisplayName("Service with multiple strategies should return true if any one detects as transient")]
    public async Task IsTransient_MultipleDetectorsOneReturnsTrue_ReturnsTrue()
    {
        var exception = new Exception("test");
        var strategy1 = CreateStrategy((exception, false));
        var strategy2 = CreateStrategy((exception, true));
        var detector = CreateDetector(strategy1, strategy2);

        await Assert.That(detector.IsTransient(exception)).IsTrue();
    }

    [Test]
    [DisplayName("Service should return false when all strategies detect the exception as non-transient")]
    public async Task IsTransient_AllDetectorsReturnFalse_ReturnsFalse()
    {
        var exception = new Exception("test");
        var strategy1 = Mock.Of<ITransientExceptionStrategy>().Object;
        var strategy2 = Mock.Of<ITransientExceptionStrategy>().Object;
        Mock.Get(strategy1).IsTransient(Arg.Any<Exception>()).Returns(false);
        Mock.Get(strategy2).IsTransient(Arg.Any<Exception>()).Returns(false);
        var detector = CreateDetector(strategy1, strategy2);

        await Assert.That(detector.IsTransient(exception)).IsFalse();
    }

    [Test]
    [DisplayName("Service should walk the inner exception chain '$exception' to find '$transientException'")]
    [MethodDataSource(nameof(InnerExceptionChainTestCases))]
    public async Task IsTransient_InnerExceptionChainHasTransient_ReturnsTrue(Exception exception, Exception transientException)
    {
        var strategy = Mock.Of<ITransientExceptionStrategy>().Object;
        Mock.Get(strategy).IsTransient(transientException).Returns(true);
        Mock.Get(strategy).IsTransient(Arg.Is<Exception>(e => e != transientException)).Returns(false);
        var detector = CreateDetector(strategy);

        await Assert.That(detector.IsTransient(exception)).IsTrue();
    }

    [Test]
    [DisplayName("Service should inspect '$aggregateException' and return $expectedResult")]
    [MethodDataSource(nameof(AggregateExceptionTestCases))]
    public async Task IsTransient_AggregateException_ChecksInnerExceptions(
        AggregateException aggregateException,
        Action<ITransientExceptionStrategy> configureDetector,
        bool expectedResult)
    {
        var strategy = Mock.Of<ITransientExceptionStrategy>().Object;
        configureDetector(strategy);
        var detector = CreateDetector(strategy);

        await Assert.That(detector.IsTransient(aggregateException)).IsEqualTo(expectedResult);
    }

    [Test]
    [DisplayName("AggregateException with mixed inner exceptions should be transient if any inner is transient")]
    public async Task IsTransient_AggregateExceptionWithMultipleInnerOneTransient_ReturnsTrue()
    {
        var transientException = new TimeoutException("timeout");
        var nonTransientException = new InvalidOperationException("invalid");
        var aggregateException = new AggregateException("aggregate", nonTransientException, transientException);

        var strategy = CreateStrategy(
            (aggregateException, false),
            (nonTransientException, false),
            (transientException, true));
        var detector = CreateDetector(strategy);

        await Assert.That(detector.IsTransient(aggregateException)).IsTrue();
    }

    public static IEnumerable<object[]> InnerExceptionChainTestCases
    {
        get
        {
            var transientException = new TimeoutException("timeout");

            yield return
            [
                new Exception("outer", transientException),
                transientException
            ];

            yield return
            [
                new Exception("outer", new("middle", transientException)),
                transientException
            ];
        }
    }

    public static IEnumerable<object[]> AggregateExceptionTestCases
    {
        get
        {
            // Scenario 1: One transient inner exception
            var timeoutException = new TimeoutException("timeout");
            yield return
            [
                new AggregateException("aggregate", timeoutException),
                (Action<ITransientExceptionStrategy>)(detector =>
                {
                    Mock.Get(detector).IsTransient(Arg.Is<Exception>(e => e is TimeoutException && e.Message == "timeout")).Returns(true);
                    Mock.Get(detector).IsTransient(Arg.Is<Exception>(e => e.GetType() != typeof(TimeoutException))).Returns(false);
                }),
                true
            ];

            // Scenario 2: No transient inner exceptions
            yield return
            [
                new AggregateException("aggregate", new InvalidOperationException("error1"), new ArgumentException("error2")),
                (Action<ITransientExceptionStrategy>)(detector =>
                {
                    Mock.Get(detector).IsTransient(Arg.Any<Exception>()).Returns(false);
                }),
                false
            ];
        }
    }

    private static ITransientExceptionStrategy CreateStrategy(params (Exception exception, bool isTransient)[] behaviors)
    {
        var detector = Mock.Of<ITransientExceptionStrategy>().Object;
        foreach (var (exception, isTransient) in behaviors)
            Mock.Get(detector).IsTransient(exception).Returns(isTransient);
        return detector;
    }

    private static TransientExceptionDetector CreateDetector(params ITransientExceptionStrategy[] detectors) => new(detectors);
}
