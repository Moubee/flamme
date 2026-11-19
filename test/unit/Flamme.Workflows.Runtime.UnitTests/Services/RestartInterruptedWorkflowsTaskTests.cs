using Flamme.Common;
using Flamme.Common.Models;
using Flamme.Common.Multitenancy;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Runtime.Options;
using Flamme.Workflows.Runtime.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Services;

public class RestartInterruptedWorkflowsTaskTests
{
    [Test]
    [DisplayName("ExecuteAsync restarts each workflow within its tenant context")]
    public async Task ExecuteAsync_RestartsWithinWorkflowTenantContext()
    {
        var workflowInstanceStore = IWorkflowInstanceStore.Mock().Object;
        var tenantAccessor = new DefaultTenantAccessor();
        var tenantService = ITenantService.Mock().Object;
        var workflowRestarter = IWorkflowRestarter.Mock().Object;
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var logger = ILogger<RestartInterruptedWorkflowsTask>.Mock().Object;
        var now = DateTimeOffset.Parse("2026-04-14T12:00:00Z");
        var workflowInstances = new List<WorkflowInstanceSummary>
        {
            CreateWorkflowInstance("workflow-1", "tenant-a", now),
            CreateWorkflowInstance("workflow-2", "tenant-b", now)
        };
        var observedTenantIds = new List<string?>();
        timeProvider.SetUtcNow(now);
        Mock.Get(tenantService).FindAsync("tenant-a", Arg.Any<CancellationToken>()).Returns(new Tenant { Id = "tenant-a", Name = "Tenant A" });
        Mock.Get(tenantService).FindAsync("tenant-b", Arg.Any<CancellationToken>()).Returns(new Tenant { Id = "tenant-b", Name = "Tenant B" });
        Mock.Get(workflowInstanceStore).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
                    .Returns(
                        (global::Flamme.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::Flamme.Common.Models.PageArgs __arg1, global::System.Threading.CancellationToken __arg2) =>
                        {
                            var pageArgs = __arg1;
                            var items = pageArgs.Offset == 0 ? workflowInstances : new List<WorkflowInstanceSummary>();
                            return new ValueTask<Page<WorkflowInstanceSummary>>(Page.Of(items, workflowInstances.Count));
                        });
        Mock.Get(workflowRestarter).RestartWorkflowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Callback(() => observedTenantIds.Add(tenantAccessor.Tenant?.Id));

        var options = Microsoft.Extensions.Options.Options.Create(new RuntimeOptions
        {
            RestartInterruptedWorkflowsBatchSize = 10,
            InactivityThreshold = TimeSpan.FromMinutes(5)
        });
        var task = new RestartInterruptedWorkflowsTask(workflowRestarter, workflowInstanceStore, logger, options, timeProvider, tenantService, tenantAccessor);

        await task.ExecuteAsync(CancellationToken.None);

        await Assert.That(observedTenantIds).IsEquivalentTo(new string?[] { "tenant-a", "tenant-b" }, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tenantAccessor.Tenant).IsNull();
        Mock.Get(workflowRestarter).RestartWorkflowAsync("workflow-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(workflowRestarter).RestartWorkflowAsync("workflow-2", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [Arguments(null, DisplayName = "ExecuteAsync does not push tenant context for a null tenant id")]
    [Arguments("", DisplayName = "ExecuteAsync does not push tenant context for an empty tenant id")]
    [Arguments("*", DisplayName = "ExecuteAsync does not push tenant context for an agnostic tenant id")]
    public async Task ExecuteAsync_DefaultOrAgnosticTenant_DoesNotPushContext(string? tenantId)
    {
        var workflowInstanceStore = IWorkflowInstanceStore.Mock().Object;
        var tenantAccessor = new DefaultTenantAccessor();
        var tenantService = ITenantService.Mock().Object;
        var workflowRestarter = IWorkflowRestarter.Mock().Object;
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var logger = ILogger<RestartInterruptedWorkflowsTask>.Mock().Object;
        var now = DateTimeOffset.Parse("2026-04-14T12:00:00Z");
        var workflowInstances = new List<WorkflowInstanceSummary>
        {
            CreateWorkflowInstance("workflow-1", tenantId, now)
        };
        var observedTenantIds = new List<string?>();
        timeProvider.SetUtcNow(now);
        Mock.Get(workflowInstanceStore).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
                    .Returns(
                        (global::Flamme.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::Flamme.Common.Models.PageArgs __arg1, global::System.Threading.CancellationToken __arg2) =>
                        {
                            var pageArgs = __arg1;
                            var items = pageArgs.Offset == 0 ? workflowInstances : new List<WorkflowInstanceSummary>();
                            return new ValueTask<Page<WorkflowInstanceSummary>>(Page.Of(items, workflowInstances.Count));
                        });
        Mock.Get(workflowRestarter).RestartWorkflowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Callback(() => observedTenantIds.Add(tenantAccessor.Tenant?.Id));

        var options = Microsoft.Extensions.Options.Options.Create(new RuntimeOptions
        {
            RestartInterruptedWorkflowsBatchSize = 10,
            InactivityThreshold = TimeSpan.FromMinutes(5)
        });
        var task = new RestartInterruptedWorkflowsTask(workflowRestarter, workflowInstanceStore, logger, options, timeProvider, tenantService, tenantAccessor);

        await task.ExecuteAsync(CancellationToken.None);

        await Assert.That(observedTenantIds).IsEquivalentTo(new string?[] { null }, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tenantAccessor.Tenant).IsNull();
        Mock.Get(tenantService).FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(workflowRestarter).RestartWorkflowAsync("workflow-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("ExecuteAsync continues restarting later workflows after a failure")]
    public async Task ExecuteAsync_ContinuesAfterFailure()
    {
        var workflowInstanceStore = IWorkflowInstanceStore.Mock().Object;
        var tenantAccessor = new DefaultTenantAccessor();
        var tenantService = ITenantService.Mock().Object;
        var workflowRestarter = IWorkflowRestarter.Mock().Object;
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var logger = ILogger<RestartInterruptedWorkflowsTask>.Mock().Object;
        var now = DateTimeOffset.Parse("2026-04-14T12:00:00Z");
        var workflowInstances = new List<WorkflowInstanceSummary>
        {
            CreateWorkflowInstance("workflow-1", "tenant-a", now),
            CreateWorkflowInstance("workflow-2", "tenant-b", now)
        };
        timeProvider.SetUtcNow(now);
        Mock.Get(tenantService).FindAsync("tenant-a", Arg.Any<CancellationToken>()).Returns(() => Task.FromException<Tenant?>(new InvalidOperationException("Transient tenant lookup failure")));
        Mock.Get(tenantService).FindAsync("tenant-b", Arg.Any<CancellationToken>()).Returns(new Tenant { Id = "tenant-b", Name = "Tenant B" });
        Mock.Get(workflowInstanceStore).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
                    .Returns(
                        (global::Flamme.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::Flamme.Common.Models.PageArgs __arg1, global::System.Threading.CancellationToken __arg2) =>
                        {
                            var pageArgs = __arg1;
                            var items = pageArgs.Offset == 0 ? workflowInstances : new List<WorkflowInstanceSummary>();
                            return new ValueTask<Page<WorkflowInstanceSummary>>(Page.Of(items, workflowInstances.Count));
                        });

        var options = Microsoft.Extensions.Options.Options.Create(new RuntimeOptions
        {
            RestartInterruptedWorkflowsBatchSize = 10,
            InactivityThreshold = TimeSpan.FromMinutes(5)
        });
        var task = new RestartInterruptedWorkflowsTask(workflowRestarter, workflowInstanceStore, logger, options, timeProvider, tenantService, tenantAccessor);

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(workflowRestarter).RestartWorkflowAsync("workflow-1", Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(workflowRestarter).RestartWorkflowAsync("workflow-2", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("ExecuteAsync restarts tenant-specific workflows when tenant services are unavailable")]
    public async Task ExecuteAsync_WithoutTenantServices_RestartsWorkflow()
    {
        var workflowInstanceStore = IWorkflowInstanceStore.Mock().Object;
        var workflowRestarter = IWorkflowRestarter.Mock().Object;
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var logger = ILogger<RestartInterruptedWorkflowsTask>.Mock().Object;
        var now = DateTimeOffset.Parse("2026-04-14T12:00:00Z");
        var workflowInstances = new List<WorkflowInstanceSummary>
        {
            CreateWorkflowInstance("workflow-1", "tenant-a", now)
        };
        timeProvider.SetUtcNow(now);
        Mock.Get(workflowInstanceStore).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
                    .Returns(
                        (global::Flamme.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::Flamme.Common.Models.PageArgs __arg1, global::System.Threading.CancellationToken __arg2) =>
                        {
                            var pageArgs = __arg1;
                            var items = pageArgs.Offset == 0 ? workflowInstances : new List<WorkflowInstanceSummary>();
                            return new ValueTask<Page<WorkflowInstanceSummary>>(Page.Of(items, workflowInstances.Count));
                        });

        var options = Microsoft.Extensions.Options.Options.Create(new RuntimeOptions
        {
            RestartInterruptedWorkflowsBatchSize = 10,
            InactivityThreshold = TimeSpan.FromMinutes(5)
        });
        var task = new RestartInterruptedWorkflowsTask(workflowRestarter, workflowInstanceStore, logger, options, timeProvider);

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(workflowRestarter).RestartWorkflowAsync("workflow-1", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    private static WorkflowInstanceSummary CreateWorkflowInstance(string id, string? tenantId, DateTimeOffset updatedAt)
    {
        return new WorkflowInstanceSummary
        {
            Id = id,
            TenantId = tenantId,
            DefinitionId = "definition",
            DefinitionVersionId = "definition:1",
            Status = WorkflowStatus.Running,
            SubStatus = WorkflowSubStatus.Pending,
            CreatedAt = updatedAt.AddMinutes(-10),
            UpdatedAt = updatedAt.AddMinutes(-10)
        };
    }
}
