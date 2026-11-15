using Elsa.Common;
using Elsa.Common.DistributedHosting;
using Elsa.Common.DistributedHosting.DistributedLocks;
using Elsa.Common.Multitenancy;
using Elsa.Mediator;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Runtime.Commands;
using Elsa.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.State;
using Medallion.Threading;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class WorkflowDispatchOutboxProcessorTests
{
    private readonly IWorkflowDispatchOutboxStore _store = Mock.Of<IWorkflowDispatchOutboxStore>().Object;
    private readonly IWorkflowInstanceStore _workflowInstanceStore = Mock.Of<IWorkflowInstanceStore>().Object;
    private readonly ICommandSender _commandSender = Mock.Of<ICommandSender>().Object;
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly WorkflowDispatcherOptions _options = new();
    private readonly WorkflowDispatchOutboxProcessor _processor;

    public WorkflowDispatchOutboxProcessorTests()
    {
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        _processor = CreateProcessor();
    }

    [Test]
    public async Task ProcessAsync_ReturnsWithoutScanning_WhenProcessorLockCannotBeAcquired()
    {
        var processor = CreateProcessor(new ContendedDistributedSynchronizationProvider());

        await processor.ProcessAsync();

        Mock.Get(_store).FindManyAsync(Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_ReturnsWithoutScanning_WhenProcessorLockAcquisitionTimesOut()
    {
        var processor = CreateProcessor(new TimeoutDistributedSynchronizationProvider());

        await processor.ProcessAsync();

        Mock.Get(_store).FindManyAsync(Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task TryProcessAsync_ReturnsFalseWithoutScanning_WhenProcessorLockAcquisitionTimesOut()
    {
        var processor = CreateProcessor(new TimeoutDistributedSynchronizationProvider());

        var result = await processor.TryProcessAsync();

        await Assert.That(result).IsFalse();
        Mock.Get(_store).FindManyAsync(Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_UsesTenantScopedProcessorLock_WhenTenantContextIsAvailable()
    {
        var lockProvider = new RecordingDistributedSynchronizationProvider();
        var tenantAccessor = new DefaultTenantAccessor();
        var processor = CreateProcessor(lockProvider, tenantAccessor);
        using var tenantScope = tenantAccessor.PushContext(new Tenant { Id = "tenant-a" });
        string? observedLockName = null;
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        observedLockName = lockProvider.CurrentLockName;
                        return [];
                    });

        await processor.ProcessAsync();

        await Assert.That(observedLockName).IsEqualTo("Elsa:WorkflowDispatchOutbox:Processor:tenant-a");
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task TryProcessAsync_UsesTenantScopedProcessorLock_WhenTenantContextIsAvailable()
    {
        var lockProvider = new RecordingDistributedSynchronizationProvider();
        var tenantAccessor = new DefaultTenantAccessor();
        var processor = CreateProcessor(lockProvider, tenantAccessor);
        using var tenantScope = tenantAccessor.PushContext(new Tenant { Id = "tenant-a" });
        string? observedLockName = null;
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        observedLockName = lockProvider.CurrentLockName;
                        return [];
                    });

        var result = await processor.TryProcessAsync();

        await Assert.That(result).IsTrue();
        await Assert.That(observedLockName).IsEqualTo("Elsa:WorkflowDispatchOutbox:Processor:tenant-a");
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    private WorkflowDispatchOutboxProcessor CreateProcessor(IDistributedLockProvider? distributedLockProvider = null, ITenantAccessor? tenantAccessor = null)
    {
        return new(
            _store,
            _workflowInstanceStore,
            _commandSender,
            distributedLockProvider ?? new NoopDistributedSynchronizationProvider(),
            _timeProvider,
            Microsoft.Extensions.Options.Options.Create(new DistributedLockingOptions()),
            Microsoft.Extensions.Options.Options.Create(_options),
            NullLogger<WorkflowDispatchOutboxProcessor>.Instance,
            tenantAccessor);
    }

    [Test]
    public async Task ProcessAsync_DoesNotDispatch_WhenOwnerWorkflowHasNotCommittedOutboxMarker()
    {
        var item = CreateItem();
        _timeProvider.SetUtcNow(item.CreatedAt.Add(_options.OrphanedOutboxItemRetention).AddTicks(-1));
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: false));

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), Any(), Any(), Any()).WasNeverCalled();
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_DeletesOutboxItem_WhenOwnerWorkflowNeverCommittedMarkerAfterRetentionPeriod()
    {
        var createdAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var item = CreateItem(createdAt);
        _timeProvider.SetUtcNow(createdAt.Add(_options.OrphanedOutboxItemRetention));
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: false));

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), Any(), Any(), Any()).WasNeverCalled();
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_DoesNotCountDeliveryFailure_WhenUncommittedCleanupDeleteFails()
    {
        var createdAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var item = CreateItem(createdAt);
        _timeProvider.SetUtcNow(createdAt.Add(_options.OrphanedOutboxItemRetention));
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: false));
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromException(new InvalidOperationException("Delete failed.")));

        await _processor.ProcessAsync();

        await Assert.That(item.DeliveryAttempts).IsEqualTo(0);
        Mock.Get(_store).SaveAsync(item, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), Any(), Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_KeepsOutboxItem_WhenOwnerWorkflowIsMissingWithinRetentionPeriod()
    {
        var createdAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var item = CreateItem(createdAt);
        _timeProvider.SetUtcNow(createdAt.Add(_options.OrphanedOutboxItemRetention).AddTicks(-1));
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns((WorkflowInstance?)null);

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), Any(), Any(), Any()).WasNeverCalled();
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_DeletesOutboxItem_WhenOwnerWorkflowIsMissingAfterRetentionPeriod()
    {
        var createdAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var item = CreateItem(createdAt);
        _timeProvider.SetUtcNow(createdAt.Add(_options.OrphanedOutboxItemRetention));
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns((WorkflowInstance?)null);

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), Any(), Any(), Any()).WasNeverCalled();
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_DoesNotCountDeliveryFailure_WhenMissingOwnerCleanupDeleteFails()
    {
        var createdAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var item = CreateItem(createdAt);
        _timeProvider.SetUtcNow(createdAt.Add(_options.OrphanedOutboxItemRetention));
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns((WorkflowInstance?)null);
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromException(new InvalidOperationException("Delete failed.")));

        await _processor.ProcessAsync();

        await Assert.That(item.DeliveryAttempts).IsEqualTo(0);
        Mock.Get(_store).SaveAsync(item, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(Arg.Is<global::Elsa.Mediator.Contracts.ICommand<global::Elsa.Mediator.Models.Unit>>(__value => __value is DispatchWorkflowDefinitionCommand), Any(), Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_DispatchesAndDeletes_WhenOwnerWorkflowCommittedOutboxMarker()
    {
        var item = CreateItem();
        var owner = CreateOwnerWorkflowInstance(includeOutboxMarker: true);
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(owner);

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            item.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_workflowInstanceStore).SaveAsync(
            Arg.Is<WorkflowInstance>(x => !x.WorkflowState.HasWorkflowDispatchOutboxItem(item.Id)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_ReloadsOwnerBeforeRemovingCommittedMarker()
    {
        var item = CreateItem();
        var staleOwner = CreateOwnerWorkflowInstance(includeOutboxMarker: true);
        var freshOwner = CreateOwnerWorkflowInstance(includeOutboxMarker: true);
        freshOwner.Name = "fresh owner";
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .ReturnsSequentially(
        staleOwner, freshOwner);

        await _processor.ProcessAsync();

        Mock.Get(_workflowInstanceStore).SaveAsync(
            Arg.Is<WorkflowInstance>(x => ReferenceEquals(x, freshOwner) && !x.WorkflowState.HasWorkflowDispatchOutboxItem(item.Id)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_SavesMarkerRemovalWhileHoldingOwnerWorkflowInstanceLock()
    {
        var lockProvider = new RecordingDistributedSynchronizationProvider();
        var processor = CreateProcessor(lockProvider);
        var item = CreateItem();
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: true));
        string? observedLockName = null;
        Mock.Get(_workflowInstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        observedLockName = lockProvider.CurrentLockName;
                        return ValueTask.CompletedTask;
                    });

        await processor.ProcessAsync();

        await Assert.That(observedLockName).IsEqualTo("workflow-instance:parent-1");
        Mock.Get(_workflowInstanceStore).SaveAsync(
            Arg.Is<WorkflowInstance>(x => !x.WorkflowState.HasWorkflowDispatchOutboxItem(item.Id)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_DoesNotRecreateOutboxItem_WhenMarkerCleanupFails()
    {
        var item = CreateItem();
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: true));
        Mock.Get(_workflowInstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>())
                    .Returns(() => throw new InvalidOperationException("Store unavailable."));

        await _processor.ProcessAsync();

        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_store).SaveAsync(item, Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_DoesNotCountDeliveryFailure_WhenDeleteAfterSendFails()
    {
        var item = CreateItem();
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: true));
        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromException(new InvalidOperationException("Delete failed.")));

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            item.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
        await Assert.That(item.DeliveryAttempts).IsEqualTo(0);
        Mock.Get(_store).SaveAsync(item, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_workflowInstanceStore).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ProcessAsync_KeepsOutboxItem_WhenDispatchFails()
    {
        var item = CreateItem();
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: true));
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(item.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None)
                    .Returns(() => Task.FromException<Elsa.Mediator.Models.Unit>(new InvalidOperationException("Queue unavailable.")));

        await _processor.ProcessAsync();

        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_store).SaveAsync(Arg.Is<WorkflowDispatchOutboxItem>(x => x.Id == item.Id && x.DeliveryAttempts == 1), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_ContinuesBatch_WhenFailurePersistenceFails()
    {
        var failedItem = CreateItem(id: "outbox-1");
        var nextItem = CreateItem(id: "outbox-2");
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([failedItem, nextItem]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .ReturnsSequentially(
        CreateOwnerWorkflowInstance(includeOutboxMarker: true, itemIds: [failedItem.Id]), CreateOwnerWorkflowInstance(includeOutboxMarker: true, itemIds: [nextItem.Id]), CreateOwnerWorkflowInstance(includeOutboxMarker: true, itemIds: [nextItem.Id]));
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(failedItem.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None)
                    .Returns(() => Task.FromException<Elsa.Mediator.Models.Unit>(new InvalidOperationException("Queue unavailable.")));
        Mock.Get(_store).SaveAsync(failedItem, Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromException(new InvalidOperationException("Save failed.")));

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            nextItem.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
        Mock.Get(_store).DeleteAsync(nextItem.Id, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_DeletesOutboxItem_WhenMaxDeliveryAttemptsIsReached()
    {
        var item = CreateItem();
        item.DeliveryAttempts = _options.MaxOutboxDeliveryAttempts - 1;
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .Returns(CreateOwnerWorkflowInstance(includeOutboxMarker: true));
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(item.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None)
                    .Returns(() => Task.FromException<Elsa.Mediator.Models.Unit>(new InvalidOperationException("Queue unavailable.")));

        await _processor.ProcessAsync();

        Mock.Get(_store).DeleteAsync(item.Id, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ProcessAsync_ContinuesBatch_WhenMaxAttemptDeleteFails()
    {
        var failedItem = CreateItem(id: "outbox-1");
        var nextItem = CreateItem(id: "outbox-2");
        failedItem.DeliveryAttempts = _options.MaxOutboxDeliveryAttempts - 1;
        Mock.Get(_store).FindManyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([failedItem, nextItem]);
        Mock.Get(_workflowInstanceStore).FindAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
                    .ReturnsSequentially(
        CreateOwnerWorkflowInstance(includeOutboxMarker: true, itemIds: [failedItem.Id]), CreateOwnerWorkflowInstance(includeOutboxMarker: true, itemIds: [nextItem.Id]), CreateOwnerWorkflowInstance(includeOutboxMarker: true, itemIds: [nextItem.Id]));
        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(failedItem.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None)
                    .Returns(() => Task.FromException<Elsa.Mediator.Models.Unit>(new InvalidOperationException("Queue unavailable.")));
        Mock.Get(_store).DeleteAsync(failedItem.Id, Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromException(new InvalidOperationException("Delete failed.")));

        await _processor.ProcessAsync();

        Mock.Get(_commandSender).SendAsync<global::Elsa.Mediator.Models.Unit>(
            nextItem.WorkflowDefinitionCommand!, CommandStrategy.Background, Arg.Any<IDictionary<object, object>>(), CancellationToken.None).WasCalled(Times.Exactly(1));
        Mock.Get(_store).SaveAsync(Arg.Is<WorkflowDispatchOutboxItem>(x => x.Id == failedItem.Id && x.DeliveryAttempts == _options.MaxOutboxDeliveryAttempts), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_store).DeleteAsync(nextItem.Id, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    private static WorkflowDispatchOutboxItem CreateItem(DateTimeOffset? createdAt = null, string id = "outbox-1")
    {
        return new()
        {
            Id = id,
            OwnerWorkflowInstanceId = "parent-1",
            Kind = WorkflowDispatchOutboxItemKind.WorkflowDefinition,
            WorkflowDefinitionCommand = new DispatchWorkflowDefinitionCommand("definition-version-1")
            {
                InstanceId = $"child-{id}"
            },
            CreatedAt = createdAt ?? new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero)
        };
    }

    private static WorkflowInstance CreateOwnerWorkflowInstance(bool includeOutboxMarker, ICollection<string>? itemIds = null)
    {
        var workflowState = new WorkflowState
        {
            Id = "parent-1",
            DefinitionId = "parent-definition",
            DefinitionVersionId = "parent-definition-version",
            Properties = includeOutboxMarker
                ? new Dictionary<string, object>
                {
                    [WorkflowDispatchOutboxStateExtensions.PropertyKey] = new WorkflowDispatchOutboxState
                    {
                        ItemIds = itemIds ?? ["outbox-1"]
                    }
                }
                : new Dictionary<string, object>()
        };

        return new()
        {
            Id = workflowState.Id,
            DefinitionId = workflowState.DefinitionId,
            DefinitionVersionId = workflowState.DefinitionVersionId,
            WorkflowState = workflowState
        };
    }

    private class ContendedDistributedSynchronizationProvider : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => new ContendedDistributedLock(name);
    }

    private class TimeoutDistributedSynchronizationProvider : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => new TimeoutDistributedLock(name);
    }

    private class RecordingDistributedSynchronizationProvider : IDistributedLockProvider
    {
        private readonly AsyncLocal<Stack<string>?> _heldLockNames = new();

        public string? CurrentLockName => _heldLockNames.Value is { Count: > 0 } heldLockNames ? heldLockNames.Peek() : null;

        public IDistributedLock CreateLock(string name) => new RecordingDistributedLock(this, name);

        private IDistributedSynchronizationHandle Acquire(string name)
        {
            var heldLockNames = _heldLockNames.Value ??= new Stack<string>();
            heldLockNames.Push(name);
            return new RecordingDistributedSynchronizationHandle(this, name);
        }

        private void Release(string name)
        {
            var heldLockNames = _heldLockNames.Value ?? throw new InvalidOperationException("No distributed locks are held.");
            var currentLockName = heldLockNames.Pop();

            if (currentLockName != name)
                throw new InvalidOperationException($"Expected to release distributed lock '{currentLockName}', but '{name}' was released.");

            if (heldLockNames.Count == 0)
                _heldLockNames.Value = null;
        }

        private class RecordingDistributedLock(RecordingDistributedSynchronizationProvider provider, string name) : IDistributedLock
        {
            public string Name => name;

            public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) => provider.Acquire(name);

            public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => provider.Acquire(name);

            public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
            {
                return new ValueTask<IDistributedSynchronizationHandle?>(provider.Acquire(name));
            }

            public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            {
                return new ValueTask<IDistributedSynchronizationHandle>(provider.Acquire(name));
            }
        }

        private class RecordingDistributedSynchronizationHandle(RecordingDistributedSynchronizationProvider provider, string name) : IDistributedSynchronizationHandle
        {
            private int _disposed;

            public CancellationToken HandleLostToken => CancellationToken.None;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    provider.Release(name);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private class ContendedDistributedLock(string name) : IDistributedLock
    {
        public string Name => name;

        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) => null;

        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new TimeoutException();

        public virtual ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            return new ValueTask<IDistributedSynchronizationHandle?>((IDistributedSynchronizationHandle?)null);
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<IDistributedSynchronizationHandle>(new TimeoutException());
        }
    }

    private class TimeoutDistributedLock(string name) : ContendedDistributedLock(name)
    {
        public override ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<IDistributedSynchronizationHandle?>(new TimeoutException());
        }
    }
}
