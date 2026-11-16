using Elsa.Workflows.Runtime.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.HealthChecks;

public class ElsaRuntimeHealthCheckTests
{
    private readonly IWorkflowRuntime _workflowRuntime = IWorkflowRuntime.Mock().Object;
    private readonly IQuiescenceSignal _quiescenceSignal = IQuiescenceSignal.Mock().Object;
    private readonly ElsaRuntimeHealthCheck _sut;

    public ElsaRuntimeHealthCheckTests()
    {
        Mock.Get(_workflowRuntime).CreateClientAsync(Arg.Any<CancellationToken>()).Returns(IWorkflowClient.Mock().Object);
        Mock.Get(_quiescenceSignal).CurrentState.Returns(QuiescenceState.Initial("test"));
        Mock.Get(_quiescenceSignal).ActiveExecutionCycleCount.Returns(0);
        _sut = new ElsaRuntimeHealthCheck(_workflowRuntime, _quiescenceSignal, NullLogger<ElsaRuntimeHealthCheck>.Instance);
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
