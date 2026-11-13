using Elsa.Common;
using Elsa.Common.Models;
using Elsa.Extensions;
using Elsa.Testing.Shared;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.Middleware.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.IntegrationTests.GracefulShutdown;

/// <summary>
/// End-to-end test for the drain orchestrator's deadline-breach path against a real workflow execution. A workflow
/// containing an activity held until cancellation is started on a background task; the orchestrator is invoked
/// mid-flight with a short deadline; the test asserts the drain-cancelled workflow stays
/// <see cref="WorkflowStatus.Finished"/> / <see cref="WorkflowSubStatus.Cancelled"/> and a
/// <c>WorkflowInterrupted</c> forensic log entry is written.
///
/// This is the integration-level companion to the unit tests in <c>DrainOrchestratorWaitTests</c> — it exercises the
/// production DI graph, the real <see cref="Pipelines.WorkflowExecution.IWorkflowExecutionPipeline"/>, and the
/// <c>ExecutionCycleHandle.Disposed</c>-await sequencing that observes the workflow runner's terminal commit
/// before the orchestrator classifies the persisted instance.
/// </summary>
public class DeadlineBreachEndToEndTests : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly IWorkflowRunner _workflowRunner;
    private readonly IDrainOrchestrator _orchestrator;

    public DeadlineBreachEndToEndTests()
    {
        _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .AddActivitiesFrom<DeadlineBreachEndToEndTests>()
            .ConfigureElsa(elsa => elsa
                .UseWorkflowRuntime(runtime => runtime.ConfigureGracefulShutdown(o =>
                {
                    // The deadline test holds its activity until drain cancellation, regardless of scheduling delay.
                    o.DrainDeadline = TimeSpan.FromMilliseconds(50);
                    o.IngressPauseTimeout = TimeSpan.FromMilliseconds(50);
                })))
            .Build();
        _workflowRunner = _services.GetRequiredService<IWorkflowRunner>();
        _orchestrator = _services.GetRequiredService<IDrainOrchestrator>();
    }

    [Test]
    [DisplayName("Drain deadline breach leaves a Finished/Cancelled instance as-is with a WorkflowInterrupted log entry and does not requeue it")]
    public async Task DeadlineBreachLeavesFinishedCancelledAndDoesNotRequeue()
    {
        await _services.PopulateRegistriesAsync();

        var activityState = new ObservableActivityState();
        var workflow = new TestWorkflow(builder => builder.Root = new ObservableActivity
        {
            State = activityState,
            WaitForCancellation = true
        });

        using var workflowCancellation = new CancellationTokenSource();

        // Start a real execution cycle that cannot finish naturally before the drain reaches its deadline.
        var workflowTask = Task.Run(() => _workflowRunner.RunAsync(workflow, cancellationToken: workflowCancellation.Token));

        try
        {
            // Wait until the activity has actually started executing.
            await activityState.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Trigger drain mid-flight. The activity stays active until this real drain cancels its cycle.
            var outcome = await _orchestrator.DrainAsync(DrainTrigger.HostStopSignal);

            // Wait for the workflow runner to fully unwind.
            try { await workflowTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { /* Force-cancellation may surface from the runner. */ }

            // Drain reports a deadline breach with exactly one force-cancelled execution cycle.
            await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.DeadlineExceeded);
            await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(1);
            await Assert.That(outcome.ForceCancelledInstanceIds).HasSingleItem();

            // The runner committed Finished/Cancelled after the force-cancel. Drain must leave that
            // row as-is (#8419): promoting it to Running/Interrupted would requeue a workflow whose
            // serialized state has no scheduled work.
            using var scope = _services.CreateScope();
            var instanceStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
            var cancelledId = (await Assert.That(outcome.ForceCancelledInstanceIds).HasSingleItem());
            var cancelled = await instanceStore.FindAsync(new WorkflowInstanceFilter { Id = cancelledId });

            await Assert.That(cancelled).IsNotNull();
            await Assert.That(cancelled.Status).IsEqualTo(WorkflowStatus.Finished);
            await Assert.That(cancelled.SubStatus).IsEqualTo(WorkflowSubStatus.Cancelled);

            var restarter = new RecordingRestarter();
            var scanner = ActivatorUtilities.CreateInstance<Elsa.Workflows.Runtime.Services.InterruptedRecoveryScanner>(scope.ServiceProvider, restarter);
            var requeued = await scanner.ScanAndRequeueAsync(CancellationToken.None);
            await Assert.That(requeued).IsEqualTo(0);
            await Assert.That(restarter.RestartedIds).IsEmpty();

            var interruptedInstances = (await instanceStore.FindManyAsync(
                new WorkflowInstanceFilter { WorkflowSubStatus = WorkflowSubStatus.Interrupted },
                CancellationToken.None)).ToList();
            await Assert.That(interruptedInstances).IsEmpty();

            // A WorkflowInterrupted forensic log entry was written for the force-cancelled execution cycle.
            var logStore = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionLogStore>();
            var logRecords = await logStore.FindManyAsync(
                new WorkflowExecutionLogRecordFilter(),
                PageArgs.FromPage(0, 100),
                CancellationToken.None);
            var interruptedLog = logRecords.Items.FirstOrDefault(r => r.EventName == WorkflowInterruptedPayload.WorkflowInterruptedEventName);

            await Assert.That(interruptedLog).IsNotNull();
            await Assert.That(interruptedLog!.Payload).IsOfType(typeof(WorkflowInterruptedPayload));
            var payload = (WorkflowInterruptedPayload)interruptedLog.Payload!;
            await Assert.That(payload.Reason).IsEqualTo(WorkflowInterruptedPayload.ReasonDeadlineBreach);
        }
        finally
        {
            if (!workflowTask.IsCompleted)
            {
                await workflowCancellation.CancelAsync();
                try { await workflowTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception exception) when (!exception.IsFatal()) { /* Preserve the original test failure while still making a bounded cleanup attempt. */ }
            }
        }
    }

    [Test]
    [DisplayName("Drain that completes within deadline reports CompletedWithinDeadline (success path baseline)")]
    public async Task SuccessPathBaseline()
    {
        await _services.PopulateRegistriesAsync();

        // No workflow running — drain finds zero active execution cycles and returns immediately.
        var outcome = await _orchestrator.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
    }

    public ValueTask DisposeAsync() => TestResourceDisposal.DisposeAsync(_services);

    private sealed class RecordingRestarter : IWorkflowRestarter
    {
        public List<string> RestartedIds { get; } = new();

        public Task RestartWorkflowAsync(string workflowInstanceId, CancellationToken cancellationToken = default)
        {
            RestartedIds.Add(workflowInstanceId);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Shared state for <see cref="ObservableActivity"/>: signals when execution started and the natural-completion / cancelled outcome.</summary>
public sealed class ObservableActivityState
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// A test activity that signals entry, then waits for cancellation or a configured duration while observing its
/// <see cref="ActivityExecutionContext.CancellationToken"/>.
/// </summary>
public class ObservableActivity : CodeActivity
{
    /// <summary>Delay in milliseconds; an integer keeps the activity serializable.</summary>
    public int DelayMs { get; set; }

    /// <summary>Holds the execution cycle until cancellation without relying on relative timer scheduling.</summary>
    public bool WaitForCancellation { get; set; }

    /// <summary>Out-of-band signalling channel for the test (set by the test before scheduling).</summary>
    public ObservableActivityState? State { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        State?.Started.TrySetResult();
        try
        {
            if (WaitForCancellation)
            {
                // Drain cancels this cycle after transitioning the workflow through context.Cancel().
                // The activity's caller token is separate, so retain it only as a cleanup fallback.
                var cycle = (ExecutionCycleHandle)context.WorkflowExecutionContext.TransientProperties[ExecutionCycleTrackingMiddleware.ExecutionCycleHandleKey];
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, cycle.CancellationToken);
                var cancellationToken = cancellation.Token;
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => cancelled.TrySetCanceled(cancellationToken));
                await cancelled.Task;
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(DelayMs), context.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            State?.Cancelled.TrySetResult();
        }
    }
}
