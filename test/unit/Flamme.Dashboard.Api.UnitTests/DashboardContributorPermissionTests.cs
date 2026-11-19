using Elsa.Common.Models;
using Elsa.Dashboard.Abstractions.Contracts;
using Elsa.Dashboard.Abstractions.Models;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Dashboard;
using static Elsa.Dashboard.Api.UnitTests.TestContributor;

namespace Elsa.Dashboard.Api.UnitTests;

/// <summary>
/// Each module guards the dashboard data it adds with the permission of that data. A contribution that declared nothing
/// would need <c>dashboard:view</c>, which hides it from the callers it is meant for.
/// </summary>
public class DashboardContributorPermissionTests
{
    private readonly DashboardContext _context = new(new(DashboardRangeKeys.TwentyFourHours, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)), false, CancellationToken.None);

    [Test]
    public async Task WorkflowContributor_GuardsRuntimeAndInstancesAndTheirFindings()
    {
        var store = CreateStore();
        var runtime = IWorkflowRuntimeAdminService.Mock().Object;
        Mock.Get(runtime).GetStatus().Returns(new RuntimeAdminStatus(
            QuiescenceState.Initial("generation") with { Reason = QuiescenceReason.AdministrativePause },
            [new("source", IngressSourceState.Running, new InvalidOperationException(), null)],
            0));

        await AssertGuardsAsync(
            new WorkflowDashboardContributor(store, runtime),
            async permissions => await Assert.That((permissions.Runtime!.Value, permissions.WorkflowInstances!.Value)).IsEqualTo((RuntimeView, InstancesView)),
            RuntimeView,
            InstancesView);
    }

    [Test]
    public async Task WorkflowContributor_GuardsItsTrendsRecentActivityAndHotspotsWithInstancesView()
    {
        var contributor = new WorkflowDashboardContributor(CreateStore(), IWorkflowRuntimeAdminService.Mock().Object);
        var range = _context.Range;

        var trends = await contributor.GetWorkflowTrendsAsync(new(range, DashboardTrendGranularity.Hour, false, CancellationToken.None));
        var activity = await contributor.GetRecentActivityAsync(new(range, 10, false, CancellationToken.None));
        var hotspots = await contributor.GetWorkflowHotspotsAsync(new(range, DashboardHotspotMetric.Faults, 10, false, CancellationToken.None));

        await Assert.That(trends!.Permission).IsEqualTo(InstancesView);
        await Assert.That(activity!.Permission).IsEqualTo(InstancesView);
        await Assert.That(hotspots!.Permission).IsEqualTo(InstancesView);
    }

    [Test]
    public async Task BuiltInContributors_DeclareEveryPermissionTheirOverviewCardsAndPanelsUse()
    {
        var runtime = IWorkflowRuntimeAdminService.Mock().Object;
        Mock.Get(runtime).GetStatus().Returns(new RuntimeAdminStatus(QuiescenceState.Initial("generation"), [], 0));
        var contributors = new IDashboardContributor[]
        {
            new WorkflowDashboardContributor(CreateStore(), runtime)
        };

        foreach (var contributor in contributors)
        {
            var overview = await contributor.GetOverviewAsync(_context);
            var declared = contributor.OverviewPermissions!.All().ToList();
            var used = overview!.Metrics.Select(x => x.Permission).Concat(overview.Panels.Select(x => x.Permission)).ToList();

            foreach (var permission in used)
            {
                await Assert.That(declared).Contains(permission!.Value);
            }
        }
    }

    [Test]
    public async Task WorkflowContributor_WithRuntimeOnlyCaller_DoesNotQueryTheInstanceStore()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(99L);
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowInstanceSummary>());
        var runtime = IWorkflowRuntimeAdminService.Mock().Object;
        Mock.Get(runtime).GetStatus().Returns(new RuntimeAdminStatus(QuiescenceState.Initial("generation"), [], 0));
        var context = _context with { CanRead = permission => permission == RuntimeView };

        var overview = await new WorkflowDashboardContributor(store, runtime).GetOverviewAsync(context);

        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        await Assert.That(overview!.Runtime!.Status).IsEqualTo(DashboardRuntimeStatusKeys.AcceptingWork);
        await Assert.That(overview.WorkflowInstances!.Running).IsEqualTo(0);
    }

    [Test]
    public async Task WorkflowContributor_WithFailingInstanceStore_KeepsRuntimeAndReportsInstancesUnavailable()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("secret connection string"));
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("secret connection string"));
        var runtime = IWorkflowRuntimeAdminService.Mock().Object;
        Mock.Get(runtime).GetStatus().Returns(new RuntimeAdminStatus(QuiescenceState.Initial("generation"), [], 0));

        var overview = await new WorkflowDashboardContributor(store, runtime).GetOverviewAsync(_context);

        await Assert.That(overview!.Runtime!.Status).IsEqualTo(DashboardRuntimeStatusKeys.AcceptingWork);
        await Assert.That(overview.WorkflowInstances!.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unavailable.Status);
        await Assert.That(overview.WorkflowInstances.Capability.Reason).DoesNotContain("secret");
    }

    private static IWorkflowInstanceStore CreateStore()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(1L);
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowInstanceSummary>());
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<PageArgs>(), Arg.Any<WorkflowInstanceOrder<DateTimeOffset?>>(), Arg.Any<CancellationToken>())
            .Returns(new Page<WorkflowInstanceSummary>([], 0));
        return store;
    }

    private async Task AssertGuardsAsync(IDashboardContributor contributor, Func<DashboardOverviewPermissions, Task> assertDeclared, params DashboardPermission[] findingPermissions)
    {
        await assertDeclared(contributor.OverviewPermissions!);

        var findings = await contributor.GetFindingsAsync(_context);

        await Assert.That(findings).IsNotEmpty();
        foreach (var finding in findings)
        {
            await Assert.That(findingPermissions).Contains(finding.Permission!.Value);
        }

        foreach (var permission in findingPermissions)
        {
            await Assert.That(findings).Contains(finding => finding.Permission == permission);
        }
    }
}
