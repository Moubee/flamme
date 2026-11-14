using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Runtime.HostedServices;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Quiescence;

public class DrainOrchestratorWaitTests : DrainOrchestratorTestsBase
{
    [Test]
    [DisplayName("Wait returns CompletedWithinDeadline when execution cycle count reaches zero before deadline")]
    public async Task ExecutionCyclesCompleteBeforeDeadline()
    {
        // Simulate two active execution cycles that drain to zero on the second poll iteration.
        var calls = 0;
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(() => calls++ switch { 0 => 2, 1 => 0, _ => 0 });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.CompletedWithinDeadline);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
    }

    [Test]
    [DisplayName("Operator force trigger always reports Forced result and force-cancels live execution cycles")]
    public async Task ForceTriggerCancelsAllCycles()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-1", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(new WorkflowInstance
                    {
                        Id = "instance-1",
                        DefinitionId = "def-1",
                        DefinitionVersionId = "ver-1",
                        Version = 1,
                        IsExecuting = true,
                    });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(1);
        await Assert.That(outcome.ForceCancelledInstanceIds).Contains("instance-1");
        await Assert.That(handle.CancellationToken.IsCancellationRequested).IsTrue();
        Mock.Get(InstanceStore).TryMarkInterruptedAsync("instance-1", Arg.Any<CancellationToken>(), false).WasCalled(Times.Exactly(1));
        Mock.Get(InstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(LogStore).AddAsync(Arg.Is<Entities.WorkflowExecutionLogRecord>(r => r.EventName == WorkflowInterruptedPayload.WorkflowInterruptedEventName), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Force drain propagates fatal CTS callback exceptions wrapped in an aggregate")]
    public async Task ForceDrainPropagatesFatalCtsCallbackExceptions()
    {
        using var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-fatal-callback", ingressSourceName: null, startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        using var registration = handle.CancellationToken.Register(() => throw new OutOfMemoryException("fatal callback failure"));
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() => new ValueTask<WorkflowInstance?>(RunningInstance("instance-fatal-callback")));

        var sut = BuildSut();
        var exception = await Assert.ThrowsExactlyAsync<AggregateException>(() => sut.DrainAsync(DrainTrigger.OperatorForce).AsTask());

        await Assert.That(exception.Flatten().InnerExceptions).Contains(inner => inner is OutOfMemoryException);
    }

    [Test]
    [DisplayName("Persistence failure during drain produces Reason=PersistenceFailure in payload")]
    public async Task PersistenceFailureRecordsReason()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-2", ingressSourceName: null, startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(new WorkflowInstance
                    {
                        Id = "instance-2",
                        DefinitionId = "def-2",
                        DefinitionVersionId = "ver-2",
                        Version = 1,
                        IsExecuting = true,
                    });
        Mock.Get(InstanceStore).TryMarkInterruptedAsync("instance-2", Arg.Any<CancellationToken>(), false)
                    .Returns(() => ValueTask.FromException<bool>(new InvalidOperationException("db unavailable")));

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        Mock.Get(LogStore).AddAsync(
            Arg.Is<Entities.WorkflowExecutionLogRecord>(r =>
                r.EventName == WorkflowInterruptedPayload.WorkflowInterruptedEventName
                && ((WorkflowInterruptedPayload)r.Payload!).Reason == WorkflowInterruptedPayload.ReasonPersistenceFailure), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Pre-cancel snapshot Finds never exceed MaxConcurrentPreCancelSnapshotFinds in flight")]
    public async Task SnapshotFindsAreCapped()
    {
        const int extra = 8;
        var cap = DrainOrchestrator.MaxConcurrentPreCancelSnapshotFinds;
        var count = cap + extra;
        var handles = Enumerable.Range(0, count)
            .Select(i => new ExecutionCycleHandle(Guid.NewGuid(), $"instance-capped-{i}", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None))
            .ToArray();
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(count);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(handles);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inFlight = 0;
        var maxInFlight = 0;
        var maxLock = new object();
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::System.Threading.CancellationToken __arg1) =>
                    {
                        var id = __arg0.Id!;
                        lock (seen)
                        {
                            if (!seen.Add(id))
                                return new ValueTask<WorkflowInstance?>(RunningInstance(id));
                        }

                        return new ValueTask<WorkflowInstance?>(WaitForSnapshotSlotAsync(id));
                    });

        async Task<WorkflowInstance?> WaitForSnapshotSlotAsync(string id)
        {
            var current = Interlocked.Increment(ref inFlight);
            lock (maxLock)
            {
                if (current > maxInFlight)
                    maxInFlight = current;
                if (maxInFlight >= cap)
                    capReached.TrySetResult();
            }

            try
            {
                await release.Task;
                return RunningInstance(id);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }

        var sut = BuildSut();
        var drainTask = sut.DrainAsync(DrainTrigger.OperatorForce).AsTask();

        await capReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        await Assert.That(maxInFlight).IsEqualTo(cap);

        release.TrySetResult();

        var outcome = await drainTask.WaitAsync(TimeSpan.FromSeconds(8));
        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(count);
        foreach (var handle in handles)
            Mock.Get(InstanceStore).TryMarkInterruptedAsync(handle.WorkflowInstanceId, Arg.Any<CancellationToken>(), false).WasCalled();
    }

    [Test]
    [DisplayName("A null pre-cancel snapshot writes WorkflowInterrupted for a later drain-induced Finished/Cancelled row without promoting it")]
    public async Task NullSnapshotDoesNotPromoteLaterCancelledInstance()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-missing", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });

        var finds = 0;
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        if (Interlocked.Increment(ref finds) == 1)
                            return new ValueTask<WorkflowInstance?>((WorkflowInstance?)null);

                        return new ValueTask<WorkflowInstance?>(CancelledInstance("instance-missing"));
                    });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(1);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync("instance-missing", Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
        Mock.Get(LogStore).AddAsync(
            Arg.Is<Entities.WorkflowExecutionLogRecord>(r =>
                r.WorkflowInstanceId == "instance-missing"
                && r.EventName == WorkflowInterruptedPayload.WorkflowInterruptedEventName), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("A confirmed Cancelled pre-cancel snapshot does not promote a later Finished/Cancelled row")]
    public async Task CancelledSnapshotDoesNotPromoteLaterCancelledInstance()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-user-cancel", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() => new ValueTask<WorkflowInstance?>(CancelledInstance("instance-user-cancel")));

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(1);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
        Mock.Get(InstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(LogStore).AddAsync(Arg.Any<Entities.WorkflowExecutionLogRecord>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("A null snapshot of an already-disposed handle does not promote a later Finished/Cancelled row")]
    public async Task DisposedHandleNullSnapshotDoesNotPromoteLaterCancelledInstance()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-already-done", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        handle.Dispose();
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });

        var finds = 0;
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        if (Interlocked.Increment(ref finds) == 1)
                            return new ValueTask<WorkflowInstance?>((WorkflowInstance?)null);

                        return new ValueTask<WorkflowInstance?>(CancelledInstance("instance-already-done"));
                    });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
        Mock.Get(InstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(LogStore).AddAsync(Arg.Any<Entities.WorkflowExecutionLogRecord>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("A disposed handle with an executing snapshot is persisted as Interrupted")]
    public async Task DisposedHandleWithExecutingSnapshotIsPersisted()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-disposed-at-checkpoint", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        handle.Dispose();
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() => new ValueTask<WorkflowInstance?>(RunningInstance("instance-disposed-at-checkpoint")));

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync("instance-disposed-at-checkpoint", Arg.Any<CancellationToken>(), false).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("A disposed handle with an active snapshot is not persisted after the row suspends")]
    public async Task DisposedHandleWithActiveSnapshotDoesNotPersistLaterSuspendedInstance()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-suspended-after-snapshot", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        handle.Dispose();
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });

        var finds = 0;
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() => Interlocked.Increment(ref finds) == 1
                        ? new ValueTask<WorkflowInstance?>(RunningInstance("instance-suspended-after-snapshot"))
                        : new ValueTask<WorkflowInstance?>(SuspendedInstance("instance-suspended-after-snapshot")));

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
        Mock.Get(InstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(LogStore).AddAsync(Arg.Any<Entities.WorkflowExecutionLogRecord>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("A disposed handle with a suspended snapshot is not persisted as Interrupted")]
    public async Task DisposedHandleWithSuspendedSnapshotIsNotPersisted()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-suspended", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        handle.Dispose();
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() => new ValueTask<WorkflowInstance?>(SuspendedInstance("instance-suspended")));

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
        Mock.Get(InstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("An active snapshot disposed after a failed cancel is recovered after settling")]
    public async Task ActiveSnapshotDisposedAfterFailedCancelIsRecoveredAfterSettling()
    {
        var targetCallbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTargetCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var target = new ExecutionCycleHandle(
            Guid.NewGuid(),
            "instance-disposed-during-settle",
            ingressSourceName: "http.trigger",
            startedAt: DateTimeOffset.UtcNow,
            linkedToken: CancellationToken.None,
            cancelCallback: () =>
            {
                targetCallbackEntered.SetResult();
                releaseTargetCallback.Task.GetAwaiter().GetResult();
            });
        var blockerCallbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlockerCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blocker = new ExecutionCycleHandle(
            Guid.NewGuid(),
            "instance-phase-a-blocker",
            ingressSourceName: "http.trigger",
            startedAt: DateTimeOffset.UtcNow,
            linkedToken: CancellationToken.None,
            cancelCallback: () =>
            {
                blockerCallbackEntered.SetResult();
                releaseBlockerCallback.Task.GetAwaiter().GetResult();
            });
        var preCancelTask = Task.Run(target.TryCancel);
        Task<DrainOutcome>? drainTask = null;

        try
        {
            await targetCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(2);
            Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { target, blocker });
            Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                            .Returns((global::Elsa.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::System.Threading.CancellationToken __arg1) => new ValueTask<WorkflowInstance?>(RunningInstance(__arg0.Id!)));

            var sut = BuildSut();
            drainTask = Task.Run(async () => await sut.DrainAsync(DrainTrigger.OperatorForce));
            await blockerCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(target.Disposed.IsCompleted).IsFalse();

            releaseBlockerCallback.TrySetResult();
            await Task.Yield();
            target.Dispose();
            releaseTargetCallback.TrySetResult();

            await Assert.That(await preCancelTask.WaitAsync(TimeSpan.FromSeconds(5))).IsFalse();
            var outcome = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
            await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(1);
            Mock.Get(InstanceStore).TryMarkInterruptedAsync(target.WorkflowInstanceId, Arg.Any<CancellationToken>(), false).WasCalled(Times.Exactly(1));
        }
        finally
        {
            // Always release synchronous callback gates so an assertion or timeout cannot strand the test host.
            releaseBlockerCallback.TrySetResult();
            releaseTargetCallback.TrySetResult();

            await ObserveCleanupAsync(preCancelTask);

            if (drainTask is not null)
                await ObserveCleanupAsync(drainTask);
        }
    }

    [Test]
    [DisplayName("A canceled drain still observes deferred disposal of a failed-cancel candidate")]
    public async Task CanceledDrainObservesDeferredCandidateDisposal()
    {
        using var drainCts = new CancellationTokenSource();
        var disposalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handle = new ExecutionCycleHandle(
            Guid.NewGuid(),
            "instance-canceled-drain-disposal",
            ingressSourceName: "http.trigger",
            startedAt: DateTimeOffset.UtcNow,
            linkedToken: CancellationToken.None,
            onDisposed: _ =>
            {
                disposalEntered.SetResult();
                releaseDisposal.Task.GetAwaiter().GetResult();
            });
        var disposeTask = Task.Run(handle.Dispose);
        var blockerCallbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlockerCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blocker = new ExecutionCycleHandle(
            Guid.NewGuid(),
            "instance-canceled-drain-blocker",
            ingressSourceName: "http.trigger",
            startedAt: DateTimeOffset.UtcNow,
            linkedToken: CancellationToken.None,
            cancelCallback: () =>
            {
                blockerCallbackEntered.SetResult();
                releaseBlockerCallback.Task.GetAwaiter().GetResult();
            });
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(2);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle, blocker });
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::System.Threading.CancellationToken __arg1) => new ValueTask<WorkflowInstance?>(RunningInstance(__arg0.Id!)));

        Task<DrainOutcome>? drainTask = null;
        try
        {
            await disposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sut = BuildSut();
            // Force-cancel invokes synchronous callbacks in Phase A. Keep the test thread available to
            // release the blocker and cancel the drain while that callback is intentionally suspended.
            drainTask = Task.Run(async () => await sut.DrainAsync(DrainTrigger.OperatorForce, drainCts.Token));
            await blockerCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await drainCts.CancelAsync();
            releaseBlockerCallback.TrySetResult();

            await Assert.ThrowsExactlyAsync<TimeoutException>(() => drainTask.WaitAsync(TimeSpan.FromSeconds(1)));
            releaseDisposal.TrySetResult();

            var outcome = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
            await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(1);
            await Assert.That(outcome.ForceCancelledInstanceIds).DoesNotContain(handle.WorkflowInstanceId);
            Mock.Get(InstanceStore).TryMarkInterruptedAsync(handle.WorkflowInstanceId, Arg.Any<CancellationToken>(), false).WasCalled(Times.Exactly(1));
        }
        finally
        {
            releaseBlockerCallback.TrySetResult();
            releaseDisposal.TrySetResult();
            await ObserveCleanupAsync(disposeTask);

            if (drainTask is not null)
                await ObserveCleanupAsync(drainTask);
        }
    }

    [Test]
    [DisplayName("A disposed handle is not persisted as Interrupted when its later row is still running")]
    public async Task DisposedHandleDoesNotPersistLaterRunningInstance()
    {
        var handle = new ExecutionCycleHandle(Guid.NewGuid(), "instance-already-running", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None);
        handle.Dispose();
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });

        var finds = 0;
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        if (Interlocked.Increment(ref finds) == 1)
                            return new ValueTask<WorkflowInstance?>((WorkflowInstance?)null);

                        return new ValueTask<WorkflowInstance?>(RunningInstance("instance-already-running"));
                    });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
        Mock.Get(InstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(LogStore).AddAsync(Arg.Any<Entities.WorkflowExecutionLogRecord>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("A cycle disposed during cancellation is not counted or persisted as drain-cancelled")]
    public async Task CycleDisposedDuringCancellationIsNotCountedOrPersisted()
    {
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new ExecutionCycleHandle(
            Guid.NewGuid(),
            "instance-completed-during-cancel",
            ingressSourceName: "http.trigger",
            startedAt: DateTimeOffset.UtcNow,
            linkedToken: CancellationToken.None,
            cancelCallback: () =>
            {
                callbackEntered.SetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
            });
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(1);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(new[] { handle });

        var finds = 0;
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        if (Interlocked.Increment(ref finds) == 1)
                            return new ValueTask<WorkflowInstance?>((WorkflowInstance?)null);

                        return new ValueTask<WorkflowInstance?>(RunningInstance("instance-completed-during-cancel"));
                    });

        var sut = BuildSut();
        var drainTask = Task.Run(async () => await sut.DrainAsync(DrainTrigger.OperatorForce));
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        handle.Dispose();
        releaseCallback.SetResult();

        var outcome = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(0);
        Mock.Get(InstanceStore).TryMarkInterruptedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
        Mock.Get(InstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(LogStore).AddAsync(Arg.Any<Entities.WorkflowExecutionLogRecord>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("Waiting for a snapshot slot does not burn the per-Find 250ms budget")]
    public async Task SnapshotQueueWaitDoesNotExcludeLaterFinds()
    {
        var cap = DrainOrchestrator.MaxConcurrentPreCancelSnapshotFinds;
        const int extra = 4;
        var count = cap + extra;
        var handles = Enumerable.Range(0, count)
            .Select(i => new ExecutionCycleHandle(Guid.NewGuid(), $"instance-queued-{i}", ingressSourceName: "http.trigger", startedAt: DateTimeOffset.UtcNow, linkedToken: CancellationToken.None))
            .ToArray();
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(count);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(handles);

        var hang = new TaskCompletionSource<WorkflowInstance?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stalledIds = new HashSet<string>(StringComparer.Ordinal);
        var promotedIds = new HashSet<string>(StringComparer.Ordinal);
        var snapshotFinds = 0;
        Mock.Get(InstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::System.Threading.CancellationToken __arg1) =>
                    {
                        var id = __arg0.Id!;
                        lock (seen)
                        {
                            // Phase C: runner committed Finished/Cancelled after force-cancel.
                            // Only ids that snapshot successfully join drainInduced and get the forensic log.
                            if (!seen.Add(id))
                                return new ValueTask<WorkflowInstance?>(CancelledInstance(id));
                        }

                        if (Interlocked.Increment(ref snapshotFinds) <= cap)
                        {
                            lock (stalledIds)
                                stalledIds.Add(id);
                            return new ValueTask<WorkflowInstance?>(hang.Task);
                        }

                        lock (promotedIds)
                            promotedIds.Add(id);
                        return new ValueTask<WorkflowInstance?>(RunningInstance(id));
                    });

        var sut = BuildSut();
        var outcome = await sut.DrainAsync(DrainTrigger.OperatorForce).AsTask().WaitAsync(TimeSpan.FromSeconds(8));

        await Assert.That(outcome.OverallResult).IsEqualTo(DrainResult.Forced);
        await Assert.That(outcome.ExecutionCyclesForceCancelledCount).IsEqualTo(count);
        await Assert.That(stalledIds.Count).IsEqualTo(cap);
        await Assert.That(promotedIds.Count).IsEqualTo(extra);
        await Assert.That(hang.Task.IsCompleted).IsFalse();

        foreach (var id in stalledIds)
        {
            Mock.Get(InstanceStore).TryMarkInterruptedAsync(id, Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
            Mock.Get(LogStore).AddAsync(
                Arg.Is<Entities.WorkflowExecutionLogRecord>(r => r.WorkflowInstanceId == id), Arg.Any<CancellationToken>()).WasNeverCalled();
        }

        foreach (var id in promotedIds)
        {
            Mock.Get(InstanceStore).TryMarkInterruptedAsync(id, Arg.Any<CancellationToken>(), Arg.Any<bool>()).WasNeverCalled();
            Mock.Get(LogStore).AddAsync(
                Arg.Is<Entities.WorkflowExecutionLogRecord>(r =>
                    r.WorkflowInstanceId == id
                    && r.EventName == WorkflowInterruptedPayload.WorkflowInterruptedEventName), Arg.Any<CancellationToken>()).WasCalled();
        }
    }

    private static WorkflowInstance RunningInstance(string id) => new()
    {
        Id = id,
        DefinitionId = "def-1",
        DefinitionVersionId = "ver-1",
        Version = 1,
        Status = WorkflowStatus.Running,
        SubStatus = WorkflowSubStatus.Executing,
        IsExecuting = true,
    };

    private static async Task ObserveCleanupAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Preserve the original assertion/timeout while observing the cleanup task.
        }
        catch (OperationCanceledException)
        {
            // Preserve the original assertion/timeout while observing the cleanup task.
        }
    }

    private static WorkflowInstance SuspendedInstance(string id) => new()
    {
        Id = id,
        DefinitionId = "def-1",
        DefinitionVersionId = "ver-1",
        Version = 1,
        Status = WorkflowStatus.Running,
        SubStatus = WorkflowSubStatus.Suspended,
        IsExecuting = false,
    };

    private static WorkflowInstance CancelledInstance(string id) => new()
    {
        Id = id,
        DefinitionId = "def-1",
        DefinitionVersionId = "ver-1",
        Version = 1,
        Status = WorkflowStatus.Finished,
        SubStatus = WorkflowSubStatus.Cancelled,
        IsExecuting = false,
    };

    [Test]
    [DisplayName("Second non-force drain in same generation throws InvalidOperationException")]
    public async Task SecondNonForceDrainThrows()
    {
        var sut = BuildSut();
        await sut.DrainAsync(DrainTrigger.HostStopSignal);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await sut.DrainAsync(DrainTrigger.HostStopSignal));
    }

    [Test]
    [DisplayName("Operator force after a completed drain returns the previous outcome with WasCached=true")]
    public async Task OperatorForceAfterPreviousReturnsCachedOutcome()
    {
        var sut = BuildSut();
        var first = await sut.DrainAsync(DrainTrigger.HostStopSignal);
        var second = await sut.DrainAsync(DrainTrigger.OperatorForce);

        await Assert.That(first.WasCached).IsFalse().Because("First (fresh) drain must not be flagged as cached.");
        await Assert.That(second.WasCached).IsTrue().Because("Second (force-after-completed) drain must be flagged as cached so the admin endpoint skips audit publishing.");
        // Same payload modulo the WasCached flag.
        await Assert.That(second).IsEqualTo(first with { WasCached = true });
    }

    [Test]
    [DisplayName("Host stop drain swallows ObjectDisposedException after shutdown cancellation")]
    public async Task HostStopDrainSwallowsObjectDisposedExceptionAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var orchestrator = Mock.Of<IDrainOrchestrator>().Object;
        Mock.Get(orchestrator).DrainAsync(DrainTrigger.HostStopSignal, cts.Token).Returns(() => ThrowObjectDisposedAsync());
        var hostedService = new DrainOrchestratorHostedService(orchestrator, Mock.Of<ILogger<DrainOrchestratorHostedService>>().Object);

        await hostedService.StopAsync(cts.Token);
    }

    [Test]
    [DisplayName("Host stop drain propagates ObjectDisposedException before shutdown cancellation")]
    public async Task HostStopDrainPropagatesObjectDisposedExceptionBeforeCancellation()
    {
        var orchestrator = Mock.Of<IDrainOrchestrator>().Object;
        Mock.Get(orchestrator).DrainAsync(DrainTrigger.HostStopSignal, CancellationToken.None).Returns(() => ThrowObjectDisposedAsync());
        var hostedService = new DrainOrchestratorHostedService(orchestrator, Mock.Of<ILogger<DrainOrchestratorHostedService>>().Object);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => hostedService.StopAsync(CancellationToken.None));
    }

    private static async ValueTask<DrainOutcome> ThrowObjectDisposedAsync()
    {
        await Task.Yield();
        throw new ObjectDisposedException("drain dependency");
    }
}
