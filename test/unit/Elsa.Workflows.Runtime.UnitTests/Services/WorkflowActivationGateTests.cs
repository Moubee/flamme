using System.Collections.Concurrent;
using Elsa.Common.DistributedHosting;
using Elsa.Common.Multitenancy;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.ActivationValidators;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class WorkflowActivationGateTests
{
    [Test]
    public async Task AllowsWithoutLock_WhenWorkflowHasNoActivationStrategy()
    {
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).Returns(true);
        var lockProvider = new InMemoryDistributedLockProvider();
        var gate = CreateGate(evaluator, lockProvider);
        var workflow = new Workflow();

        await using var lease = await gate.EvaluateAsync(workflow, "order-1");

        await Assert.That(lease.CanStart).IsTrue();
        await Assert.That(lockProvider.LockNames).IsEmpty();
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Is<WorkflowActivationStrategyEvaluationContext>(
            context => context.Workflow == workflow && context.CorrelationId == "order-1")).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ReturnsDenied_WhenEvaluatorDisallows()
    {
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).Returns(false);
        var lockProvider = new InMemoryDistributedLockProvider();
        var gate = CreateGate(evaluator, lockProvider);

        var lease = await gate.EvaluateAsync(CreateWorkflow(typeof(CorrelatedSingletonStrategy)), "order-1");

        await Assert.That(lease.CanStart).IsFalse();
        await Assert.That(lockProvider.HeldLocks).IsEqualTo(0);
    }

    [Test]
    public async Task ReleasesActivationLock_WhenEvaluatorThrows()
    {
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>())
                    .Returns(() => Task.FromException<bool>(new InvalidOperationException("evaluation failed")));
        var lockProvider = new InMemoryDistributedLockProvider();
        var gate = CreateGate(evaluator, lockProvider);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gate.EvaluateAsync(CreateWorkflow(typeof(SingletonStrategy)), null));

        await Assert.That(lockProvider.HeldLocks).IsEqualTo(0);
    }

    [Test]
    public async Task ReleasesActivationLock_WhenEvaluatorIsCancelled()
    {
        using var cancellationSource = new CancellationTokenSource();
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>())
                    .Returns(() =>
                    {
                        cancellationSource.Cancel();
                        return Task.FromCanceled<bool>(cancellationSource.Token);
                    });
        var lockProvider = new InMemoryDistributedLockProvider();
        var gate = CreateGate(evaluator, lockProvider);

        await Assert.ThrowsAsync<OperationCanceledException>(() => gate.EvaluateAsync(CreateWorkflow(typeof(SingletonStrategy)), null, cancellationSource.Token));

        await Assert.That(lockProvider.HeldLocks).IsEqualTo(0);
    }

    [Test]
    public async Task CancelsEvaluatorAndRejectsActivation_WhenDistributedLeaseIsLostDuringEvaluation()
    {
        using var handleLostSource = new CancellationTokenSource();
        var evaluatorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeEvaluation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken strategyToken = default;
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>())
                    .Returns((global::Elsa.Workflows.Runtime.WorkflowActivationStrategyEvaluationContext __arg0) =>
                    {
                        strategyToken = __arg0.CancellationToken;
                        evaluatorStarted.SetResult();
                        return completeEvaluation.Task;
                    });
        var lockProvider = new HandleLostTokenDistributedLockProvider(handleLostSource.Token);
        var gate = CreateGate(evaluator, lockProvider);

        var evaluation = gate.EvaluateAsync(CreateWorkflow(typeof(SingletonStrategy)), null);
        await evaluatorStarted.Task;
        handleLostSource.Cancel();
        var strategyObservedCancellation = strategyToken.IsCancellationRequested;
        completeEvaluation.SetResult(true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => evaluation);
        await Assert.That(strategyObservedCancellation).IsTrue();
        await Assert.That(lockProvider.HandleDisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task CancelsLeaseToken_WhenDistributedLeaseIsLostAfterEvaluation()
    {
        using var handleLostSource = new CancellationTokenSource();
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).Returns(true);
        var lockProvider = new HandleLostTokenDistributedLockProvider(handleLostSource.Token);
        var gate = CreateGate(evaluator, lockProvider);

        await using var lease = await gate.EvaluateAsync(CreateWorkflow(typeof(SingletonStrategy)), null);
        await Assert.That(lease.CanStart).IsTrue();
        var cancellationToken = lease.GetEffectiveCancellationToken(CancellationToken.None);
        await Assert.That(cancellationToken.IsCancellationRequested).IsFalse();

        handleLostSource.Cancel();

        await Assert.That(cancellationToken.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task SerializesCheckAndCreate_WhenTwoCallersRaceOnSameCorrelation()
    {
        var started = 0;
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>())
                    .Returns(() => Interlocked.CompareExchange(ref started, 1, 0) == 0);
        var lockProvider = new InMemoryDistributedLockProvider();
        var gate = CreateGate(evaluator, lockProvider);
        var workflow = CreateWorkflow(typeof(CorrelatedSingletonStrategy));

        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await using var lease = await gate.EvaluateAsync(workflow, "order-1");
            firstReady.SetResult();
            await releaseFirst.Task;
            return lease.CanStart;
        });

        await firstReady.Task;

        var second = Task.Run(async () =>
        {
            await using var lease = await gate.EvaluateAsync(workflow, "order-1");
            return lease.CanStart;
        });

        await lockProvider.Contended.Task;
        await Assert.That(second.IsCompleted).IsFalse();
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).WasCalled(Times.Exactly(1));

        releaseFirst.SetResult();
        var results = await Task.WhenAll(first, second);

        await Assert.That(results).Contains(true);
        await Assert.That(results).Contains(false);
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).WasCalled(Times.Exactly(2));
    }

    [Test]
    public async Task BuiltInLockKeys_AreHashedDeterministicAndScopedByTenantAndStrategy()
    {
        const string tenantA = "tenant-secret-a";
        const string tenantB = "tenant-secret-b";
        const string definitionId = "definition-secret";
        const string correlationId = "correlation-secret";
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).Returns(true);
        var lockProvider = new InMemoryDistributedLockProvider();
        var singleton = CreateWorkflow(typeof(SingletonStrategy), definitionId);
        var correlatedSingleton = CreateWorkflow(typeof(CorrelatedSingletonStrategy), definitionId);
        var correlation = CreateWorkflow(typeof(CorrelationStrategy), definitionId);

        await using (await CreateGate(evaluator, lockProvider, tenantA).EvaluateAsync(singleton, correlationId)) { }
        await using (await CreateGate(evaluator, lockProvider, tenantA).EvaluateAsync(singleton, correlationId)) { }
        await using (await CreateGate(evaluator, lockProvider, tenantA).EvaluateAsync(correlatedSingleton, correlationId)) { }
        await using (await CreateGate(evaluator, lockProvider, tenantA).EvaluateAsync(correlation, correlationId)) { }
        await using (await CreateGate(evaluator, lockProvider, tenantB).EvaluateAsync(singleton, correlationId)) { }
        await using (await CreateGate(evaluator, lockProvider, tenantB).EvaluateAsync(correlatedSingleton, correlationId)) { }
        await using (await CreateGate(evaluator, lockProvider, tenantB).EvaluateAsync(correlation, correlationId)) { }

        var keys = lockProvider.LockNames.ToArray();
        await Assert.That(keys.Length).IsEqualTo(7);
        await Assert.That(keys[1]).IsEqualTo(keys[0]);
        await Assert.That(keys.Skip(1).Distinct().Count()).IsEqualTo(6);
        foreach (var key in keys)
        {
            await Assert.That(key).DoesNotContain(tenantA).WithComparison(StringComparison.Ordinal);
            await Assert.That(key).DoesNotContain(tenantB).WithComparison(StringComparison.Ordinal);
            await Assert.That(key).DoesNotContain(definitionId).WithComparison(StringComparison.Ordinal);
            await Assert.That(key).DoesNotContain(correlationId).WithComparison(StringComparison.Ordinal);
            await Assert.That(key).Matches("^workflow-activation:v1:[a-z-]+:[A-F0-9]{64}$");
        }
    }

    [Test]
    public async Task UsesDefaultTenantScope_WhenTenantAccessorIsNotRegistered()
    {
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).Returns(true);
        var lockProvider = new InMemoryDistributedLockProvider();
        using var serviceProvider = new ServiceCollection()
            .AddSingleton<IWorkflowActivationStrategyEvaluator>(evaluator)
            .AddSingleton<IDistributedLockProvider>(lockProvider)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new DistributedLockingOptions()))
            .AddScoped<WorkflowActivationGate>()
            .BuildServiceProvider();
        using var serviceScope = serviceProvider.CreateScope();
        var unregisteredAccessorGate = serviceScope.ServiceProvider.GetRequiredService<WorkflowActivationGate>();
        var tenantAccessor = new DefaultTenantAccessor();
        var defaultTenantGate = new WorkflowActivationGate(
            evaluator,
            lockProvider,
            Microsoft.Extensions.Options.Options.Create(new DistributedLockingOptions()),
            tenantAccessor);
        var workflow = CreateWorkflow(typeof(SingletonStrategy));

        await using (await unregisteredAccessorGate.EvaluateAsync(workflow, null)) { }
        await using (await defaultTenantGate.EvaluateAsync(workflow, null)) { }
        using (tenantAccessor.PushContext(new Tenant { Id = "tenant-b", Name = "Tenant B" }))
        await using (await defaultTenantGate.EvaluateAsync(workflow, null)) { }

        var keys = lockProvider.LockNames.ToArray();
        await Assert.That(keys.Length).IsEqualTo(3);
        await Assert.That(keys[1]).IsEqualTo(keys[0]);
        await Assert.That(keys[2]).IsNotEqualTo(keys[1]);
        await Assert.That(keys[2]).DoesNotContain("tenant-b").WithComparison(StringComparison.Ordinal);
    }

    [Test]
    public async Task CustomStrategy_IsEvaluatedWithoutInventedDistributedScope()
    {
        var evaluator = IWorkflowActivationStrategyEvaluator.Mock().Object;
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).Returns(true);
        var lockProvider = new InMemoryDistributedLockProvider();
        var gate = CreateGate(evaluator, lockProvider);

        await using var lease = await gate.EvaluateAsync(CreateWorkflow(typeof(CustomStrategy)), "corr-1");

        await Assert.That(lease.CanStart).IsTrue();
        await Assert.That(lockProvider.LockNames).IsEmpty();
        Mock.Get(evaluator).CanStartWorkflowAsync(Arg.Any<WorkflowActivationStrategyEvaluationContext>()).WasCalled(Times.Exactly(1));
    }

    private static WorkflowActivationGate CreateGate(IWorkflowActivationStrategyEvaluator evaluator, IDistributedLockProvider lockProvider, string tenantId = "tenant-a")
    {
        var tenantAccessor = ITenantAccessor.Mock().Object;
        Mock.Get(tenantAccessor).TenantId.Returns(tenantId);
        return new(
            evaluator,
            lockProvider,
            Microsoft.Extensions.Options.Options.Create(new DistributedLockingOptions()),
            tenantAccessor);
    }

    private static Workflow CreateWorkflow(Type strategyType, string definitionId = "def-1") => new()
    {
        Identity = new WorkflowIdentity(definitionId, 1, definitionId),
        Options = new WorkflowOptions { ActivationStrategyType = strategyType }
    };

    private sealed class CustomStrategy : IWorkflowActivationStrategy
    {
        public ValueTask<bool> GetAllowActivationAsync(WorkflowInstantiationStrategyContext context) => ValueTask.FromResult(true);
    }

    private sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private readonly ConcurrentQueue<string> _lockNames = new();

        public int HeldLocks => _locks.Values.Count(semaphore => semaphore.CurrentCount == 0);
        public IReadOnlyCollection<string> LockNames => _lockNames.ToArray();
        public TaskCompletionSource Contended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDistributedLock CreateLock(string name)
        {
            _lockNames.Enqueue(name);
            return new InMemoryDistributedLock(_locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1)), name, Contended);
        }
    }

    private sealed class HandleLostTokenDistributedLockProvider(CancellationToken handleLostToken) : IDistributedLockProvider
    {
        private HandleLostTokenDistributedSynchronizationHandle Handle { get; } = new(handleLostToken);
        public int HandleDisposeCount => Handle.DisposeCount;

        public IDistributedLock CreateLock(string name) => new HandleLostTokenDistributedLock(name, Handle);

        private sealed class HandleLostTokenDistributedLock(string name, HandleLostTokenDistributedSynchronizationHandle handle) : IDistributedLock
        {
            public string Name => name;

            public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) => handle;

            public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => handle;

            public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult<IDistributedSynchronizationHandle?>(handle);

            public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult<IDistributedSynchronizationHandle>(handle);
        }

        private sealed class HandleLostTokenDistributedSynchronizationHandle(CancellationToken handleLostToken) : IDistributedSynchronizationHandle
        {
            private int _disposeCount;

            public CancellationToken HandleLostToken => handleLostToken;
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose() => Interlocked.Increment(ref _disposeCount);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class InMemoryDistributedLock(SemaphoreSlim semaphore, string name, TaskCompletionSource contended) : IDistributedLock
    {
        public string Name => name;

        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = new(), CancellationToken cancellationToken = new()) =>
            semaphore.Wait(timeout, cancellationToken) ? new Handle(semaphore) : null;

        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = new())
        {
            if (!semaphore.Wait(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken))
                throw new TimeoutException($"Could not acquire lock '{name}'.");

            return new Handle(semaphore);
        }

        public async ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = new(), CancellationToken cancellationToken = new())
        {
            SignalContention();
            return await semaphore.WaitAsync(timeout, cancellationToken) ? new Handle(semaphore) : null;
        }

        public async ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = new())
        {
            SignalContention();
            if (!await semaphore.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken))
                throw new TimeoutException($"Could not acquire lock '{name}'.");

            return new Handle(semaphore);
        }

        private void SignalContention()
        {
            if (semaphore.CurrentCount == 0)
                contended.TrySetResult();
        }

        private sealed class Handle(SemaphoreSlim semaphore) : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;

            public void Dispose() => semaphore.Release();

            public ValueTask DisposeAsync()
            {
                semaphore.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
