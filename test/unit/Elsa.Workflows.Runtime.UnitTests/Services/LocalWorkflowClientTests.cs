using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Exceptions;
using Elsa.Workflows.Management.Mappers;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Management.Options;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime.Exceptions;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.ActivationValidators;
using Elsa.Workflows.State;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class LocalWorkflowClientTests
{
    private readonly IWorkflowInstanceManager _workflowInstanceManager = Mock.Of<IWorkflowInstanceManager>().Object;
    private readonly IWorkflowDefinitionService _workflowDefinitionService = Mock.Of<IWorkflowDefinitionService>().Object;
    private readonly IWorkflowRunner _workflowRunner = Mock.Of<IWorkflowRunner>().Object;
    private readonly IWorkflowCanceler _workflowCanceler = Mock.Of<IWorkflowCanceler>().Object;
    private readonly IWorkflowActivationGate _workflowActivationGate = Mock.Of<IWorkflowActivationGate>().Object;
    private readonly WorkflowStateMapper _workflowStateMapper = Mock.Of<WorkflowStateMapper>().Object;
    private readonly ILogger<LocalWorkflowClient> _logger = Mock.Of<ILogger<LocalWorkflowClient>>().Object;

    [Test]
    public async Task CreateInstanceAsync_ThrowsWorkflowDefinitionNotFoundException_WhenDefinitionDoesNotExist()
    {
        // Arrange
        var client = CreateClient();
        var definitionHandle = WorkflowDefinitionHandle.ByDefinitionId("non-existent-definition");
        var request = new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = definitionHandle
        };

        var findResult = new WorkflowGraphFindResult(null, null);
        Mock.Get(_workflowDefinitionService).TryFindWorkflowGraphAsync(definitionHandle, Arg.Any<CancellationToken>())
                    .Returns(findResult);

        // Act & Assert
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionNotFoundException>(() =>
            client.CreateInstanceAsync(request));
    }

    [Test]
    public async Task CreateInstanceAsync_ThrowsWorkflowMaterializerNotFoundException_WhenMaterializerNotAvailable()
    {
        // Arrange
        var client = CreateClient();
        var definitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition");
        var request = new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = definitionHandle
        };

        var definition = new WorkflowDefinition
        {
            Id = "def-1",
            DefinitionId = "test-definition",
            MaterializerName = "unavailable-materializer"
        };

        var findResult = new WorkflowGraphFindResult(definition, null); // Null graph indicates materializer not available
        Mock.Get(_workflowDefinitionService).TryFindWorkflowGraphAsync(definitionHandle, Arg.Any<CancellationToken>())
                    .Returns(findResult);

        // Act & Assert
        await Assert.ThrowsExactlyAsync<WorkflowMaterializerNotFoundException>(() => client.CreateInstanceAsync(request));
    }

    [Test]
    public async Task CreateAndRunInstanceAsync_ThrowsWorkflowDefinitionNotFoundException_WhenDefinitionDoesNotExist()
    {
        // Arrange
        var client = CreateClient();
        var definitionHandle = WorkflowDefinitionHandle.ByDefinitionId("non-existent-definition");
        var request = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = definitionHandle
        };

        var findResult = new WorkflowGraphFindResult(null, null);
        Mock.Get(_workflowDefinitionService).TryFindWorkflowGraphAsync(definitionHandle, Arg.Any<CancellationToken>())
                    .Returns(findResult);

        // Act & Assert
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionNotFoundException>(() =>
            client.CreateAndRunInstanceAsync(request));
    }

    [Test]
    public async Task CreateAndRunInstanceAsync_ThrowsWorkflowMaterializerNotFoundException_WhenMaterializerNotAvailable()
    {
        // Arrange
        var client = CreateClient();
        var definitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition");
        var request = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = definitionHandle
        };

        var definition = new WorkflowDefinition
        {
            Id = "def-1",
            DefinitionId = "test-definition",
            MaterializerName = "unavailable-materializer"
        };

        var findResult = new WorkflowGraphFindResult(definition, null);
        Mock.Get(_workflowDefinitionService).TryFindWorkflowGraphAsync(definitionHandle, Arg.Any<CancellationToken>())
                    .Returns(findResult);

        // Act & Assert
        await Assert.ThrowsExactlyAsync<WorkflowMaterializerNotFoundException>(() =>
            client.CreateAndRunInstanceAsync(request));
    }

    [Test]
    public async Task CreateInstanceAsync_ReturnsCannotStart_WhenActivationGateDenies()
    {
        var client = CreateClient(canStart: false);
        var request = new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition"),
            CorrelationId = "order-1"
        };
        SetupWorkflowGraph("test-definition");

        var response = await client.CreateInstanceAsync(request);

        await Assert.That(response.CannotStart).IsTrue();
        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task CreateInstanceAsync_DoesNotPersist_WhenDistributedLeaseIsLostBeforeSave()
    {
        using var handleLostSource = new CancellationTokenSource();
        var lockHandle = new TrackingAsyncDisposable();
        var client = CreateClient();
        SetupWorkflowGraph("test-definition");
        Mock.Get(_workflowActivationGate).EvaluateAsync(Arg.Any<Workflow>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        handleLostSource.Cancel();
                        return Task.FromResult(new WorkflowActivationLease(true, lockHandle, handleLostSource.Token));
                    });

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition")
        }));

        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Any()).WasNeverCalled();
        await Assert.That(lockHandle.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task CreateInstanceAsync_CancelsPersistence_WhenDistributedLeaseIsLostDuringSave()
    {
        using var handleLostSource = new CancellationTokenSource();
        var lockHandle = new TrackingAsyncDisposable();
        var client = CreateClient();
        SetupWorkflowGraph("test-definition");
        Mock.Get(_workflowActivationGate).EvaluateAsync(Arg.Any<Workflow>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromResult(new WorkflowActivationLease(true, lockHandle, handleLostSource.Token)));
        var saveStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Entities.WorkflowInstance __arg0, global::System.Threading.CancellationToken __arg1) =>
                    {
                        var cancellationToken = __arg1;
                        saveStarted.SetResult(cancellationToken);
                        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        cancellationToken.Register(() => canceled.TrySetCanceled(cancellationToken));
                        return canceled.Task;
                    });

        var save = client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition")
        });
        var persistenceToken = await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handleLostSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => save);
        await Assert.That(persistenceToken.IsCancellationRequested).IsTrue();
        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(lockHandle.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task CreateInstanceAsync_PreservesCallerCancellation_WhenActivationGateReturnsLegacyLease()
    {
        using var cancellationSource = new CancellationTokenSource();
        var client = CreateClient();
        SetupWorkflowGraph("test-definition");
        Mock.Get(_workflowActivationGate).EvaluateAsync(Arg.Any<Workflow>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(new WorkflowActivationLease(true, null));
        var saveStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Entities.WorkflowInstance __arg0, global::System.Threading.CancellationToken __arg1) =>
                    {
                        var cancellationToken = __arg1;
                        saveStarted.SetResult(cancellationToken);
                        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        cancellationToken.Register(() => canceled.TrySetCanceled(cancellationToken));
                        return canceled.Task;
                    });

        var save = client.CreateInstanceAsync(new CreateWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition")
        }, cancellationSource.Token);
        var persistenceToken = await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(persistenceToken).IsEqualTo(cancellationSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => save);
    }

    [Test]
    public async Task CreateAndRunInstanceAsync_ReturnsCannotStart_WhenActivationGateDenies()
    {
        var client = CreateClient(canStart: false);
        var request = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition"),
            CorrelationId = "order-1"
        };
        SetupWorkflowGraph("test-definition");

        var response = await client.CreateAndRunInstanceAsync(request);

        await Assert.That(response.CannotStart).IsTrue();
        await Assert.That(response.WorkflowInstanceId).IsNull();
        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Any()).WasNeverCalled();
        Mock.Get(_workflowInstanceManager).CreateAndCommitWorkflowInstanceAsync(Any(), Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task CreateAndRunInstanceAsync_LogsSafeStructuredActivationDenialDetails()
    {
        const string correlationId = "sensitive-correlation-123";
        var logger = new RecordingLogger<LocalWorkflowClient>();
        var client = CreateClient(canStart: false, logger);
        SetupWorkflowGraph("test-definition", typeof(CorrelatedSingletonStrategy));
        var request = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition"),
            CorrelationId = correlationId
        };

        var response = await client.CreateAndRunInstanceAsync(request);

        await Assert.That(response.CannotStart).IsTrue();
        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Any()).WasNeverCalled();
        Mock.Get(_workflowInstanceManager).CreateAndCommitWorkflowInstanceAsync(Any(), Any(), Any()).WasNeverCalled();
        var entry = await Assert.That(logger.Entries).HasSingleItem();
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Properties["WorkflowDefinitionId"]).IsEqualTo("test-definition");
        await Assert.That(entry.Properties["WorkflowDefinitionVersionId"]).IsEqualTo("test-definition");
        await Assert.That(entry.Properties["WorkflowDefinitionVersion"]).IsEqualTo(1);
        await Assert.That(entry.Properties["ActivationStrategyType"]).IsEqualTo(typeof(CorrelatedSingletonStrategy).FullName);
        await Assert.That((bool)entry.Properties["CorrelationIdPresent"]!).IsTrue();
        await Assert.That(entry.Message).DoesNotContain(correlationId).WithComparison(StringComparison.Ordinal);
        await Assert.That(string.Join(";", entry.Properties.Values)).DoesNotContain(correlationId).WithComparison(StringComparison.Ordinal);
    }

    [Test]
    public async Task CreateAndRunInstanceAsync_DoesNotPersistProvisionalInstance_WhenRunnerThrows()
    {
        var client = CreateClient();
        SetupWorkflowGraph("test-definition");
        Mock.Get(_workflowInstanceManager).CreateWorkflowInstance(Arg.Any<Workflow>(), Arg.Any<WorkflowInstanceOptions?>())
                    .Returns(CreateRunningInstance());
        Mock.Get(_workflowRunner).RunAsync(Arg.Any<WorkflowGraph>(), Arg.Any<WorkflowState>(), Arg.Any<RunWorkflowOptions?>(), Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromException<RunWorkflowResult>(new InvalidOperationException("execution failed")));
        var request = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition"),
            CorrelationId = "correlation-1"
        };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.CreateAndRunInstanceAsync(request));

        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Any()).WasNeverCalled();
        Mock.Get(_workflowInstanceManager).CreateAndCommitWorkflowInstanceAsync(Any(), Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task CreateAndRunInstanceAsync_DoesNotPersistProvisionalInstance_WhenCancelledDuringRun()
    {
        var client = CreateClient();
        SetupWorkflowGraph("test-definition");
        Mock.Get(_workflowInstanceManager).CreateWorkflowInstance(Arg.Any<Workflow>(), Arg.Any<WorkflowInstanceOptions?>())
                    .Returns(CreateRunningInstance());
        Mock.Get(_workflowRunner).RunAsync(Arg.Any<WorkflowGraph>(), Arg.Any<WorkflowState>(), Arg.Any<RunWorkflowOptions?>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Models.WorkflowGraph __arg0, global::Elsa.Workflows.State.WorkflowState __arg1, global::Elsa.Workflows.Options.RunWorkflowOptions? __arg2, global::System.Threading.CancellationToken __arg3) => Task.FromCanceled<RunWorkflowResult>(__arg3));
        var request = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition"),
            CorrelationId = "correlation-1"
        };
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.CreateAndRunInstanceAsync(request, cancellationSource.Token));

        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Any()).WasNeverCalled();
        Mock.Get(_workflowInstanceManager).CreateAndCommitWorkflowInstanceAsync(Any(), Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task CreateAndRunInstanceAsync_CancelsRunner_WhenDistributedLeaseIsLostDuringRun()
    {
        using var handleLostSource = new CancellationTokenSource();
        var lockHandle = new TrackingAsyncDisposable();
        var client = CreateClient();
        SetupWorkflowGraph("test-definition");
        Mock.Get(_workflowInstanceManager).CreateWorkflowInstance(Arg.Any<Workflow>(), Arg.Any<WorkflowInstanceOptions?>())
                    .Returns(CreateRunningInstance());
        var runnerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerCompletion = new TaskCompletionSource<RunWorkflowResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken runnerToken = default;
        Mock.Get(_workflowRunner).RunAsync(Arg.Any<WorkflowGraph>(), Arg.Any<WorkflowState>(), Arg.Any<RunWorkflowOptions?>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Models.WorkflowGraph __arg0, global::Elsa.Workflows.State.WorkflowState __arg1, global::Elsa.Workflows.Options.RunWorkflowOptions? __arg2, global::System.Threading.CancellationToken __arg3) =>
                    {
                        runnerToken = __arg3;
                        runnerToken.Register(() => runnerCompletion.TrySetCanceled(runnerToken));
                        runnerStarted.SetResult();
                        return runnerCompletion.Task;
                    });
        Mock.Get(_workflowActivationGate).EvaluateAsync(Arg.Any<Workflow>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromResult(new WorkflowActivationLease(true, lockHandle, handleLostSource.Token)));

        var run = client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId("test-definition")
        });
        await runnerStarted.Task;
        handleLostSource.Cancel();
        var runnerObservedLeaseLoss = runnerToken.IsCancellationRequested;
        if (!runnerObservedLeaseLoss)
            runnerCompletion.TrySetException(new InvalidOperationException("The runner did not receive the lease-loss cancellation token."));

        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        await Assert.That(runnerObservedLeaseLoss).IsTrue();
        await Assert.That(lockHandle.DisposeCount).IsEqualTo(1);
        Mock.Get(_workflowInstanceManager).SaveAsync(Arg.Any<WorkflowInstance>(), Any()).WasNeverCalled();
        Mock.Get(_workflowInstanceManager).CreateAndCommitWorkflowInstanceAsync(Any(), Any(), Any()).WasNeverCalled();
    }

    private static WorkflowInstance CreateRunningInstance() => new()
    {
        Id = "test-workflow-instance-id",
        DefinitionId = "test-definition",
        DefinitionVersionId = "test-definition",
        Status = WorkflowStatus.Running,
        WorkflowState = new WorkflowState { Id = "test-workflow-instance-id", Status = WorkflowStatus.Running }
    };

    private void SetupWorkflowGraph(string definitionId, Type? activationStrategyType = null)
    {
        var workflow = new Workflow
        {
            Id = definitionId,
            Identity = new WorkflowIdentity(definitionId, 1, definitionId),
            Options = new WorkflowOptions { ActivationStrategyType = activationStrategyType }
        };
        var node = new ActivityNode(workflow, "Root");
        var graph = new WorkflowGraph(workflow, node, [node]);
        var definition = new WorkflowDefinition
        {
            Id = definitionId,
            DefinitionId = definitionId
        };
        Mock.Get(_workflowDefinitionService).TryFindWorkflowGraphAsync(Arg.Any<WorkflowDefinitionHandle>(), Arg.Any<CancellationToken>())
                    .Returns(new WorkflowGraphFindResult(definition, graph));
    }

    private LocalWorkflowClient CreateClient(bool canStart = true, ILogger<LocalWorkflowClient>? logger = null)
    {
        Mock.Get(_workflowActivationGate).EvaluateAsync(Arg.Any<Workflow>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Activities.Workflow __arg0, string? __arg1, global::System.Threading.CancellationToken __arg2) => canStart
                        ? new WorkflowActivationLease(true, null, __arg2)
                        : WorkflowActivationLease.Denied);

        return new(
            "test-workflow-instance-id",
            _workflowInstanceManager,
            _workflowDefinitionService,
            _workflowRunner,
            _workflowCanceler,
            _workflowActivationGate,
            _workflowStateMapper,
            logger ?? _logger);
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new(logLevel, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);
}
