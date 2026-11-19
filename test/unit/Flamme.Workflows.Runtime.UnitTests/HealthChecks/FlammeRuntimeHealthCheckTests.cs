using Flamme.Workflows.Runtime.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.HealthChecks;

public class FlammeRuntimeHealthCheckTests
{
    private readonly IWorkflowRuntime _workflowRuntime = IWorkflowRuntime.Mock().Object;
    private readonly IQuiescenceSignal _quiescenceSignal = IQuiescenceSignal.Mock().Object;
    private readonly FlammeRuntimeHealthCheck _sut;

    public FlammeRuntimeHealthCheckTests()
    {
        Mock.Get(_workflowRuntime).CreateClientAsync(Arg.Any<CancellationToken>()).Returns(IWorkflowClient.Mock().Object);
        Mock.Get(_quiescenceSignal).CurrentState.Returns(QuiescenceState.Initial("test"));
        Mock.Get(_quiescenceSignal).ActiveExecutionCycleCount.Returns(0);
        _sut = new FlammeRuntimeHealthCheck(_workflowRuntime, _quiescenceSignal, NullLogger<FlammeRuntimeHealthCheck>.Instance);
    }

    [Test]
    public async Task ReturnsHealthyWhenRuntimeAcceptsNewWork()
    {
        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["category"]).IsEqualTo("runtime");
        await Assert.That((bool)result.Data["acceptingNewWork"]).IsTrue();
    }

    [Test]
    public async Task ReturnsDegradedWhenRuntimeIsPaused()
    {
        Mock.Get(_quiescenceSignal).CurrentState.Returns(QuiescenceState.Initial("test") with
        {
            Reason = QuiescenceReason.AdministrativePause
        });

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["reason"]).IsEqualTo("AdministrativePause");
        await Assert.That((bool)result.Data["acceptingNewWork"]).IsFalse();
    }

    [Test]
    public async Task ReturnsUnhealthyWhenRuntimeClientCannotBeCreated()
    {
        Mock.Get(_workflowRuntime).CreateClientAsync(Arg.Any<CancellationToken>()).Returns(() => throw new InvalidOperationException("boom"));

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["category"]).IsEqualTo("runtime");
    }
}
