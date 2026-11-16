using Elsa.Common.Models;
using Elsa.Common.Multitenancy;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Recovery;

public class InterruptedRecoveryScannerTests
{
    private readonly IWorkflowRestarter _restarter = IWorkflowRestarter.Mock().Object;
    private readonly IWorkflowInstanceStore _instanceStore = IWorkflowInstanceStore.Mock().Object;
    private readonly ILogger<InterruptedRecoveryScanner> _logger = ILogger<InterruptedRecoveryScanner>.Mock().Object;
    private readonly RuntimeOptions _runtimeOptions = new() { RestartInterruptedWorkflowsBatchSize = 10 };

    [Test]
    [DisplayName("Scan filters by SubStatus = Interrupted and Status = Running")]
    public async Task FiltersByInterruptedAndRunning()
    {
        StubInstances(new[] { Summary("a") });
        var sut = BuildSut();

        await sut.ScanAndRequeueAsync(CancellationToken.None);

        Mock.Get(_instanceStore).SummarizeManyAsync(
            Arg.Is<WorkflowInstanceFilter>(f =>
                f.WorkflowSubStatus == WorkflowSubStatus.Interrupted
                && f.WorkflowStatus == WorkflowStatus.Running), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>()).WasCalled();
    }

    [Test]
    [DisplayName("Scan calls IWorkflowRestarter once per matching instance and returns the count")]
    public async Task RestartsEachInstance()
    {
        StubInstances(new[] { Summary("a"), Summary("b"), Summary("c") });
        var sut = BuildSut();

        var count = await sut.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(count).IsEqualTo(3);
        Mock.Get(_restarter).RestartWorkflowAsync("a", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_restarter).RestartWorkflowAsync("b", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_restarter).RestartWorkflowAsync("c", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Per-instance failures are swallowed; remaining instances still requeued")]
    public async Task SwallowsPerInstanceFailures()
    {
        StubInstances(new[] { Summary("a"), Summary("b"), Summary("c") });
        Mock.Get(_restarter).RestartWorkflowAsync("b", Arg.Any<CancellationToken>())
                    .Returns(() => throw new InvalidOperationException("boom"));
        var sut = BuildSut();

        var count = await sut.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(count).IsEqualTo(2); // 'b' failed
        Mock.Get(_restarter).RestartWorkflowAsync("a", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_restarter).RestartWorkflowAsync("c", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("No interrupted instances → returns 0 without invoking restarter")]
    public async Task EmptyResultSet()
    {
        StubInstances(Array.Empty<WorkflowInstanceSummary>());
        var sut = BuildSut();

        var count = await sut.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(count).IsEqualTo(0);
        Mock.Get(_restarter).RestartWorkflowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("Tenant context is pushed for tenant-scoped instances")]
    public async Task PushesTenantContext()
    {
        StubInstances(new[] { Summary("a", tenantId: "tenant-1") });
        var tenantService = ITenantService.Mock().Object;
        Mock.Get(tenantService).FindAsync("tenant-1", Arg.Any<CancellationToken>()).Returns(new Tenant { Id = "tenant-1", Name = "Tenant 1" });
        var tenantAccessor = new DefaultTenantAccessor();
        var observedTenant = (string?)null;
        Mock.Get(_restarter).RestartWorkflowAsync("a", Arg.Any<CancellationToken>()).Callback(() => observedTenant = tenantAccessor.Tenant?.Id);

        var sut = BuildSut(tenantService, tenantAccessor);
        await sut.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(observedTenant).IsEqualTo("tenant-1");
        await Assert.That(tenantAccessor.Tenant).IsNull(); // popped after the call
    }

    [Test]
    [DisplayName("Default or agnostic tenant ids do NOT push tenant context")]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("*")]
    public async Task DefaultOrAgnosticTenant(string? tenantId)
    {
        StubInstances(new[] { Summary("a", tenantId: tenantId) });
        var tenantService = ITenantService.Mock().Object;
        var tenantAccessor = new DefaultTenantAccessor();
        var observedTenant = (Tenant?)tenantAccessor.Tenant;
        Mock.Get(_restarter).RestartWorkflowAsync("a", Arg.Any<CancellationToken>()).Callback(() => observedTenant = tenantAccessor.Tenant);

        var sut = BuildSut(tenantService, tenantAccessor);
        await sut.ScanAndRequeueAsync(CancellationToken.None);

        await Assert.That(observedTenant).IsNull();
        Mock.Get(tenantService).FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    private InterruptedRecoveryScanner BuildSut(ITenantService? tenantService = null, ITenantAccessor? tenantAccessor = null) =>
        new(_restarter, _instanceStore, Microsoft.Extensions.Options.Options.Create(_runtimeOptions), _logger, tenantService, tenantAccessor);

    private void StubInstances(IReadOnlyList<WorkflowInstanceSummary> all)
    {
        Mock.Get(_instanceStore).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::Elsa.Common.Models.PageArgs __arg1, global::System.Threading.CancellationToken __arg2) =>
                    {
                        var pageArgs = __arg1;
                        ICollection<WorkflowInstanceSummary> items = pageArgs.Offset == 0
                            ? all.ToList()
                            : new List<WorkflowInstanceSummary>();
                        return new ValueTask<Page<WorkflowInstanceSummary>>(Page.Of(items, all.Count));
                    });
    }

    private static WorkflowInstanceSummary Summary(string id, string? tenantId = null) => new()
    {
        Id = id,
        TenantId = tenantId,
        DefinitionId = "def-" + id,
        DefinitionVersionId = "ver-" + id,
        Version = 1,
        SubStatus = WorkflowSubStatus.Interrupted,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
