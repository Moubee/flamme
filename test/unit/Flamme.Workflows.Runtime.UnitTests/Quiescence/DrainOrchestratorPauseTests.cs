using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Quiescence;

public class DrainOrchestratorPauseTests : DrainOrchestratorTestsBase
{
    [Test]
    [DisplayName("Empty registry: drain completes within deadline")]
    public async Task EmptyRegistry()
    {
        var sut = BuildSut();

        var outcome = await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        await Assert.That(outcome.Sources).IsEmpty();
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
    }

    [Test]
    [DisplayName("All sources pause successfully — outcome is CompletedWithinDeadline")]
    public async Task AllSourcesPauseCleanly()
    {
        var a = CreateSource("a");
        var b = CreateSource("b");
        Mock.Get(Registry).Sources.Returns(new[] { a, b });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        Mock.Get(a).PauseAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(b).PauseAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Source that throws is recorded as PauseFailed; other sources still complete")]
    public async Task ThrowingSourceIsIsolated()
    {
        var ok = CreateSource("ok");
        var bad = CreateSource("bad", pauseImpl: _ => throw new InvalidOperationException("bad day"));
        Mock.Get(Registry).Sources.Returns(new[] { ok, bad });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        Mock.Get(Registry).MarkPauseFailedAsync("bad", "exception", Arg.Any<Exception?>()).WasCalled(Times.Exactly(1));
        Mock.Get(ok).PauseAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Source that hangs past per-source timeout is recorded as PauseFailed (timeout)")]
    public async Task HangingSourceIsTimedOut()
    {
        var hanger = CreateSource(
            "hanger",
            pauseTimeout: TimeSpan.FromMilliseconds(50),
            pauseImpl: ct =>
            {
                // Awaits the linked CT — when it fires, we observe the cancellation.
                return new ValueTask(Task.Delay(TimeSpan.FromSeconds(30), ct));
            });
        Mock.Get(Registry).Sources.Returns(new[] { hanger });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        Mock.Get(Registry).MarkPauseFailedAsync("hanger", "timeout", Arg.Any<Exception?>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Source that implements IForceStoppable is force-stopped after pause timeout")]
    public async Task ForceStoppableSourceIsEscalated()
    {
        var combined = Mock.Of<IIngressSource, IForceStoppable>().Object;
        Mock.Get(combined).Name.Returns("kafka");
        Mock.Get(combined).PauseTimeout.Returns(TimeSpan.FromMilliseconds(50));
        Mock.Get(combined).CurrentState.Returns(IngressSourceState.Running);
        Mock.Get(combined).PauseAsync(Arg.Any<CancellationToken>()).Returns((global::System.Threading.CancellationToken __arg0) => new ValueTask(Task.Delay(TimeSpan.FromSeconds(30), __arg0)));
        Mock.Get(combined).ForceStopAsync(Arg.Any<CancellationToken>()).Returns(() => ValueTask.CompletedTask);
        Mock.Get(Registry).Sources.Returns(new[] { combined });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        Mock.Get(combined).ForceStopAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Source without IForceStoppable is left in PauseFailed without escalation")]
    public async Task NonForceStoppableSourceNotEscalated()
    {
        var bad = CreateSource("bad", pauseImpl: _ => throw new InvalidOperationException("nope"));
        Mock.Get(Registry).Sources.Returns(new[] { bad });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        // No force-stop interface means no escalation possible — drain still completes.
        Mock.Get(Registry).MarkPauseFailedAsync("bad", "exception", Arg.Any<Exception?>()).WasCalled(Times.Exactly(1));
    }
}
