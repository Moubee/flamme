using Elsa.Common;
using Elsa.Common.Models;
using Elsa.Extensions;
using Elsa.Mediator.HostedServices;
using Elsa.Testing.Shared;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.IntegrationTests.GracefulShutdown;

/// <summary>
/// End-to-end drain recovery using the production <see cref="IWorkflowRestarter"/> (not a recording fake).
/// A recoverable instance runs a first step, then blocks on a gated activity (still Running). Drain marks
/// that live row Interrupted. After the real restarter resumes it, the remaining work completes and the
/// first step is not re-run. A separate drain-cancelled instance stays Finished/Cancelled across scans.
/// </summary>
public class DrainRecoveryEndToEndTests : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly IWorkflowRunner _workflowRunner;
    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IDrainOrchestrator _orchestrator;
    private readonly CapturingTextWriter _capturingTextWriter = new();
    private readonly ResumeGate _resumeGate = new();

    public DrainRecoveryEndToEndTests()
    {
        _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .WithCapturingTextWriter(_capturingTextWriter)
            .ConfigureServices(services => services.AddSingleton(_resumeGate))
            .AddActivitiesFrom<DrainRecoveryEndToEndTests>()
            .AddWorkflow<RecoverableResumeWorkflow>()
            .ConfigureElsa(elsa => elsa
                .UseWorkflowRuntime(runtime => runtime.ConfigureGracefulShutdown(o =>
                {
                    o.DrainDeadline = TimeSpan.FromMilliseconds(50);
                    o.IngressPauseTimeout = TimeSpan.FromMilliseconds(50);
                })))
            .Build();
        _workflowRunner = _services.GetRequiredService<IWorkflowRunner>();
        _workflowRuntime = _services.GetRequiredService<IWorkflowRuntime>();
        _orchestrator = _services.GetRequiredService<IDrainOrchestrator>();
    }

    public ValueTask DisposeAsync() => TestResourceDisposal.DisposeAsync(_services);

    [Test]
    [DisplayName("Real restarter resumes a drain-marked Running/Interrupted instance without re-running completed work, and does not resume a drain-cancelled instance")]
    public async Task RealRestarterResumesInterruptedRunningAndLeavesDrainCancelledAlone()
    {
        await _services.PopulateRegistriesAsync();

        var recoverableClient = await _workflowRuntime.CreateClientAsync();
        var created = await recoverableClient.CreateInstanceAsync(new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(RecoverableResumeWorkflow), VersionOptions.Published)
        });
        await Assert.That(created.CannotStart).IsFalse();

        var recoverableRun = Task.Run(() => recoverableClient.RunInstanceAsync(RunWorkflowInstanceRequest.Empty));
        BackgroundCommandSenderHostedService? commandProcessor = null;
        var drainLive = new DrainLiveGate();
        Task? drainTask = null;
        try
        {
            await _resumeGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var arrangeScope = _services.CreateScope();
            var instanceStore = arrangeScope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
            await WaitUntilAsync(async () =>
            {
                var current = await instanceStore.FindAsync(new WorkflowInstanceFilter { Id = recoverableClient.WorkflowInstanceId });
                return current is { Status: WorkflowStatus.Running }
                       && current.WorkflowState.ActivityExecutionContexts.Any(context => context.IsExecuting)
                       && _capturingTextWriter.Lines.Contains("first");
            }, TimeSpan.FromSeconds(5));

            await Assert.That(_capturingTextWriter.Lines.ToList()).IsEquivalentTo(["first"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

            var drainWorkflow = new TestWorkflow(builder => builder.Root = new DrainLiveActivity { Gate = drainLive });
            drainTask = Task.Run(() => _workflowRunner.RunAsync(drainWorkflow));
            await drainLive.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var outcome = await _orchestrator.DrainAsync(DrainTrigger.HostStopSignal);

            // Drain marks the workflow Cancelled but does not cancel ActivityExecutionContext.CancellationToken.
            // Release only after DrainAsync so this cycle cannot finish before force-cancel.
            drainLive.Continue.TrySetResult();
            try { await drainTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (!ex.IsFatal()) { /* runner may complete normally or surface OCE */ }

            await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.DeadlineExceeded);
            await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(2);
            await Assert.That(outcome.ForceCancelledInstanceIds).Contains(recoverableClient.WorkflowInstanceId);
            var cancelledId = (await Assert.That(outcome.ForceCancelledInstanceIds.Where(id => id != recoverableClient.WorkflowInstanceId)).HasSingleItem());

            var interrupted = await instanceStore.FindAsync(new WorkflowInstanceFilter { Id = recoverableClient.WorkflowInstanceId });
            await Assert.That(interrupted).IsNotNull();
            await Assert.That(interrupted.Status).IsEqualTo(WorkflowStatus.Running);
            await Assert.That(interrupted.SubStatus).IsEqualTo(WorkflowSubStatus.Interrupted);
            await Assert.That(interrupted.IsExecuting).IsFalse();
            await Assert.That(_capturingTextWriter.Lines.ToList()).IsEquivalentTo(["first"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

            var cancelled = await instanceStore.FindAsync(new WorkflowInstanceFilter { Id = cancelledId });
            await Assert.That(cancelled).IsNotNull();
            await Assert.That(cancelled.Status).IsEqualTo(WorkflowStatus.Finished);
            await Assert.That(cancelled.SubStatus).IsEqualTo(WorkflowSubStatus.Cancelled);

            commandProcessor = _services.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
            await commandProcessor.StartAsync(CancellationToken.None);

            using var recoverScope = _services.CreateScope();
            var scanner = recoverScope.ServiceProvider.GetRequiredService<IInterruptedRecoveryScanner>();
            var requeued = await scanner.ScanAndRequeueAsync(CancellationToken.None);
            await Assert.That(requeued).IsEqualTo(1);

            await WaitUntilAsync(async () =>
            {
                var current = await instanceStore.FindAsync(new WorkflowInstanceFilter { Id = recoverableClient.WorkflowInstanceId });
                return current is { Status: WorkflowStatus.Finished, SubStatus: WorkflowSubStatus.Finished };
            }, TimeSpan.FromSeconds(10));

            var recovered = await instanceStore.FindAsync(new WorkflowInstanceFilter { Id = recoverableClient.WorkflowInstanceId });
            await Assert.That(recovered).IsNotNull();
            await Assert.That(recovered.Status).IsEqualTo(WorkflowStatus.Finished);
            await Assert.That(recovered.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
            await Assert.That(_capturingTextWriter.Lines.Count(line => line == "first")).IsEqualTo(1);
            await Assert.That(_capturingTextWriter.Lines).Contains("second");

            var secondScan = await scanner.ScanAndRequeueAsync(CancellationToken.None);
            await Assert.That(secondScan).IsEqualTo(0);

            var stillCancelled = await instanceStore.FindAsync(new WorkflowInstanceFilter { Id = cancelledId });
            await Assert.That(stillCancelled).IsNotNull();
            await Assert.That(stillCancelled.Status).IsEqualTo(WorkflowStatus.Finished);
            await Assert.That(stillCancelled.SubStatus).IsEqualTo(WorkflowSubStatus.Cancelled);
        }
        finally
        {
            drainLive.Continue.TrySetResult();
            if (drainTask is not null)
            {
                try { await drainTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception ex) when (!ex.IsFatal()) { /* runner may complete normally or surface OCE */ }
            }

            _resumeGate.Continue.TrySetResult();
            try { await recoverableRun.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (!ex.IsFatal()) { /* original cycle may surface OCE after drain */ }
            if (commandProcessor is not null)
                await commandProcessor.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Condition was not met within {timeout}.");
    }
}

/// <summary>Gate for <see cref="DrainLiveActivity"/> so the drain-cancelled workflow stays live until after force-cancel.</summary>
public sealed class DrainLiveGate
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Stays executing until the test releases <see cref="DrainLiveGate.Continue"/>. Drain force-cancel marks the
/// workflow Cancelled but does not cancel <see cref="ActivityExecutionContext.CancellationToken"/>, so a delay
/// on that token cannot keep the cycle live.
/// </summary>
public class DrainLiveActivity : CodeActivity
{
    public DrainLiveGate? Gate { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var gate = Gate ?? throw new InvalidOperationException("DrainLiveActivity.Gate must be set.");
        gate.Started.TrySetResult();
        await gate.Continue.Task;
    }
}

/// <summary>Gate shared by <see cref="GatedWaitActivity"/> so the test can persist mid-execution and then release remaining work.</summary>
public sealed class ResumeGate
{
    public int ExecutionCount;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Blocks without observing cancellation so drain can mark the still-Running row Interrupted.
/// The first execution persists through <see cref="IWorkflowInstanceManager"/> (not
/// <see cref="WorkflowExecutionContext.CommitAsync"/>, which would dispose the live cycle handle)
/// and then stays blocked until the test releases <see cref="ResumeGate.Continue"/>, so it cannot
/// overwrite the recovered Finished row. The restarter's re-entry completes immediately and runs
/// the remaining work.
/// </summary>
public class GatedWaitActivity : CodeActivity
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var resumeGate = context.GetRequiredService<ResumeGate>();
        var execution = Interlocked.Increment(ref resumeGate.ExecutionCount);
        if (execution == 1)
        {
            var manager = context.GetRequiredService<IWorkflowInstanceManager>();
            await manager.SaveAsync(context.WorkflowExecutionContext);
            resumeGate.Started.TrySetResult();
            await resumeGate.Continue.Task;
            return;
        }

        resumeGate.Started.TrySetResult();
    }
}

/// <summary>First step records progress, then a gated wait, then remaining work.</summary>
public class RecoverableResumeWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine("first"),
                new GatedWaitActivity(),
                new WriteLine("second")
            }
        };
    }
}
