using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.GracefulShutdown;

/// <summary>
/// Integration tests for the activation-time recovery of <see cref="WorkflowSubStatus.Interrupted"/> instances
/// (Phase 5 / US3). The scanner runs against the real <see cref="IWorkflowInstanceStore"/> — backed by the in-memory
/// store in this test setup — and exercises the same code path as production.
/// </summary>
public class InterruptedRecoveryIntegrationTests : IAsyncDisposable
{
    private readonly IServiceProvider _services;

    public InterruptedRecoveryIntegrationTests()
    {
        _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .ConfigureFlamme(flamme => flamme.UseWorkflowRuntime())
            .Build();
    }

    [Test]
    [DisplayName("ScanAndRequeueAsync requeues 100% of Interrupted instances (SC-003): $count")]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(100)]
    public async Task RequeuesAllInterruptedInstances(int count)
    {
        var fakeRestarter = new RecordingRestarter();
        using var scope = _services.CreateScope();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        await SeedInstancesAsync(instanceStore, count, WorkflowSubStatus.Interrupted, isExecuting: false);

        var scanner = ActivatorUtilities.CreateInstance<Flamme.Workflows.Runtime.Services.InterruptedRecoveryScanner>(scope.ServiceProvider, fakeRestarter);
        var requeued = await scanner.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(requeued).IsEqualTo(count);
        await Assert.That(fakeRestarter.RestartedIds.Count).IsEqualTo(count);
    }

    [Test]
    [DisplayName("Scan ignores instances NOT in Interrupted sub-status (T071 — filter purity)")]
    public async Task FilterPurity()
    {
        var fakeRestarter = new RecordingRestarter();
        using var scope = _services.CreateScope();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();

        await SeedInstancesAsync(instanceStore, 3, WorkflowSubStatus.Interrupted, isExecuting: false, idPrefix: "interrupted-");
        await SeedInstancesAsync(instanceStore, 2, WorkflowSubStatus.Suspended, isExecuting: false, idPrefix: "suspended-");
        await SeedInstancesAsync(instanceStore, 2, WorkflowSubStatus.Cancelled, isExecuting: false, idPrefix: "cancelled-");
        await SeedInstancesAsync(instanceStore, 1, WorkflowSubStatus.Faulted, isExecuting: false, idPrefix: "faulted-");
        // Stale-executing instance — handled by the existing RestartInterruptedWorkflowsTask, not by the new scan.
        await SeedInstancesAsync(instanceStore, 1, WorkflowSubStatus.Executing, isExecuting: true, idPrefix: "stale-");

        var scanner = ActivatorUtilities.CreateInstance<Flamme.Workflows.Runtime.Services.InterruptedRecoveryScanner>(scope.ServiceProvider, fakeRestarter);
        var requeued = await scanner.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(requeued).IsEqualTo(3);
        foreach (var id in fakeRestarter.RestartedIds)
            await Assert.That(id).StartsWith("interrupted-", StringComparison.CurrentCulture);
    }

    [Test]
    [DisplayName("Scan does NOT touch IsExecuting=true instances (T069 — disjoint from RestartInterruptedWorkflowsTask)")]
    public async Task DisjointFromTimeoutBasedRecovery()
    {
        var fakeRestarter = new RecordingRestarter();
        using var scope = _services.CreateScope();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();

        // The two recovery paths use disjoint filters:
        //   - RestartInterruptedWorkflowsTask (recurring): IsExecuting=true AND UpdatedAt < threshold AND Status=Running
        //   - RecoverInterruptedWorkflowsStartupTask (this scan): SubStatus = Interrupted AND Status = Running (IsExecuting=false)
        await SeedInstancesAsync(instanceStore, 2, WorkflowSubStatus.Interrupted, isExecuting: false, idPrefix: "graceful-");
        await SeedInstancesAsync(instanceStore, 2, WorkflowSubStatus.Executing, isExecuting: true, idPrefix: "ungraceful-");

        var scanner = ActivatorUtilities.CreateInstance<Flamme.Workflows.Runtime.Services.InterruptedRecoveryScanner>(scope.ServiceProvider, fakeRestarter);
        var requeued = await scanner.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(requeued).IsEqualTo(2);
        foreach (var id in fakeRestarter.RestartedIds)
            await Assert.That(id).StartsWith("graceful-", StringComparison.CurrentCulture);

        // The 'ungraceful-' instances remain in the store, untouched, available for the timeout-based task.
        var stillExecuting = await instanceStore.FindManyAsync(new WorkflowInstanceFilter { IsExecuting = true }, CancellationToken.None);
        await Assert.That(stillExecuting.Count()).IsEqualTo(2);
    }

    [Test]
    [DisplayName("Scan does NOT requeue Finished+Interrupted instances (issue #8052)")]
    public async Task DoesNotRequeueFinishedInterruptedInstances()
    {
        var fakeRestarter = new RecordingRestarter();
        using var scope = _services.CreateScope();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();

        await SeedInstancesAsync(instanceStore, 2, WorkflowSubStatus.Interrupted, isExecuting: false, idPrefix: "running-", status: WorkflowStatus.Running);
        await SeedInstancesAsync(instanceStore, 3, WorkflowSubStatus.Interrupted, isExecuting: false, idPrefix: "finished-", status: WorkflowStatus.Finished);

        var scanner = ActivatorUtilities.CreateInstance<Flamme.Workflows.Runtime.Services.InterruptedRecoveryScanner>(scope.ServiceProvider, fakeRestarter);
        var requeued = await scanner.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(requeued).IsEqualTo(2);
        foreach (var id in fakeRestarter.RestartedIds)
        {
            await Assert.That(id).StartsWith("running-");
        }

        var stillFinishedInterrupted = await instanceStore.FindManyAsync(
            new WorkflowInstanceFilter
            {
                WorkflowSubStatus = WorkflowSubStatus.Interrupted,
                WorkflowStatus = WorkflowStatus.Finished,
            },
            CancellationToken.None);
        await Assert.That(stillFinishedInterrupted.Count()).IsEqualTo(3);
    }

    private static async Task SeedInstancesAsync(
        IWorkflowInstanceStore store,
        int count,
        WorkflowSubStatus subStatus,
        bool isExecuting,
        string idPrefix = "instance-",
        WorkflowStatus status = WorkflowStatus.Running)
    {
        for (var i = 0; i < count; i++)
        {
            await store.SaveAsync(new WorkflowInstance
            {
                Id = $"{idPrefix}{i}",
                DefinitionId = "def-1",
                DefinitionVersionId = "ver-1",
                Version = 1,
                Status = status,
                SubStatus = subStatus,
                IsExecuting = isExecuting,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkflowState = new Workflows.State.WorkflowState
                {
                    Id = $"{idPrefix}{i}",
                    DefinitionId = "def-1",
                    DefinitionVersionId = "ver-1",
                    Status = status,
                    SubStatus = subStatus,
                },
            }, CancellationToken.None);
        }
    }

    /// <summary>Captures restart calls without actually invoking the workflow runtime — keeps the integration test focused.</summary>
    private sealed class RecordingRestarter : IWorkflowRestarter
    {
        public List<string> RestartedIds { get; } = new();

        public Task RestartWorkflowAsync(string workflowInstanceId, CancellationToken cancellationToken = default)
        {
            RestartedIds.Add(workflowInstanceId);
            return Task.CompletedTask;
        }
    }

    public ValueTask DisposeAsync() => TestResourceDisposal.DisposeAsync(_services);
}
