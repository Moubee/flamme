using Microsoft.Extensions.Time.Testing;
using Elsa.Dashboard.Abstractions.Contracts;
using Elsa.Dashboard.Abstractions.Models;
using Elsa.Dashboard.Api.Services;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Dashboard;
using Elsa.Workflows.Runtime.Dashboard.Extensions;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Dashboard.Api.UnitTests.TestContributor;

namespace Elsa.Dashboard.Api.UnitTests;

public class DefaultDashboardProviderTests
{
    private readonly DateTimeOffset _now = new(2026, 06, 01, 12, 00, 00, TimeSpan.Zero);

    [Test]
    public async Task GetOverviewAsync_WithNoContributors_ReturnsStableEmptySnapshot()
    {
        var provider = CreateProvider();

        var overview = await provider.GetOverviewAsync(new(DashboardRangeKeys.TwentyFourHours));

        await Assert.That(overview.BackendName).IsEqualTo("Elsa.TestHost");
        await Assert.That(overview.EnvironmentName).IsEqualTo("Integration");
        await Assert.That(overview.Runtime.Status).IsEqualTo(DashboardRuntimeStatusKeys.Unavailable);
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(0);
        await Assert.That(overview.Metrics).IsEmpty();
        await Assert.That(overview.Panels).IsEmpty();
    }

    [Test]
    public async Task GetOverviewAsync_ComposesWorkflowAndPanelContributors()
    {
        var provider = CreateProvider(
            new TestContributor("panels", 200)
            {
                Overview = new()
                {
                    Panels = [new() { Id = "custom", Title = "Custom" }]
                }
            },
            new TestContributor("workflows", 100)
            {
                Overview = new()
                {
                    Runtime = new()
                    {
                        Status = DashboardRuntimeStatusKeys.AcceptingWork,
                        IsAcceptingWork = true
                    },
                    WorkflowInstances = new()
                    {
                        Running = 3,
                        Completed = 5,
                        Faulted = 1
                    }
                }
            });

        var overview = await provider.GetOverviewAsync(new(DashboardRangeKeys.TwentyFourHours));

        await Assert.That(overview.Runtime.Status).IsEqualTo(DashboardRuntimeStatusKeys.AcceptingWork);
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(3);
        await Assert.That(overview.WorkflowInstances.Completed).IsEqualTo(5);
        await Assert.That(overview.WorkflowInstances.Faulted).IsEqualTo(1);
        await Assert.That(overview.Panels.Single().Id).IsEqualTo("custom");
    }

    public static IEnumerable<(DashboardPermission, SectionAccess)> SinglePermissions() => SectionAccess.BySinglePermission;

    [Test]
    [MethodDataSource(nameof(SinglePermissions))]
    public async Task GetOverviewAsync_WithCallerWhoMayReadOnlyOneSection_WithholdsEveryOtherSection(DashboardPermission permission, SectionAccess readable)
    {
        var provider = CreateProvider(Declared());

        var overview = await provider.GetOverviewAsync(new() { CanRead = x => x == permission });

        await readable.AssertOn(overview);
        await Assert.That(overview.BackendName).IsEqualTo("Elsa.TestHost");
        await Assert.That(overview.Metrics.Select(x => x.Id)).IsEquivalentTo(readable.Metrics, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(overview.Panels.Select(x => x.Id)).IsEquivalentTo(readable.Panels, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetOverviewAsync_WithCallerWhoMayReadTheWholeOverview_ReturnsEverything()
    {
        var provider = CreateProvider(Declared());

        var overview = await provider.GetOverviewAsync(new() { CanRead = _ => true });

        await SectionAccess.All.AssertOn(overview);
        await Assert.That(overview.Metrics.Count).IsEqualTo(3);
        await Assert.That(overview.Panels.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetOverviewAsync_WithSectionsThatDeclareNoPermission_NeedsTheWholeOverviewPermission()
    {
        var provider = CreateProvider(Undeclared());

        var narrow = await provider.GetOverviewAsync(new() { CanRead = permission => permission == InstancesView });
        var whole = await provider.GetOverviewAsync(new() { CanRead = permission => permission == WholeOverview });

        await SectionAccess.None.AssertOn(narrow);
        await Assert.That(narrow.Metrics).IsEmpty();
        await SectionAccess.All.AssertOn(whole);
        await Assert.That(whole.Metrics).HasSingleItem();
    }

    [Test]
    public async Task GetOverviewAsync_WithCallerWhoMayReadNothing_WithholdsEverySectionAndTheDeploymentNames()
    {
        var provider = CreateProvider(Declared());

        var overview = await provider.GetOverviewAsync(new(DashboardRangeKeys.SevenDays) { CanRead = _ => false });

        await SectionAccess.None.AssertOn(overview);
        await Assert.That(overview.BackendName).IsNull();
        await Assert.That(overview.EnvironmentName).IsNull();
        await Assert.That(overview.AppliedRange).IsEqualTo(DashboardRangeKeys.SevenDays);
    }

    [Test]
    public async Task GetOverviewAsync_WithCardOnlyTheCallerMayRead_InvokesTheContributorThatDeclaredItsPermission()
    {
        var contributor = new TestContributor("cards", 1)
        {
            Overview = new() { Metrics = [new() { Id = "card", Label = "Card", Permission = InstancesView }] },
            OverviewPermissions = new() { Runtime = RuntimeView, WorkflowInstances = InstancesView }
        };

        var overview = await CreateProvider(contributor).GetOverviewAsync(new() { CanRead = x => x == InstancesView });

        await Assert.That(contributor.Invocations).IsEqualTo(1);
        await Assert.That(overview.Metrics.Select(x => x.Id)).IsEquivalentTo(["card"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetOverviewAsync_WithCardPermissionTheContributorDidNotDeclare_SkipsItForACallerHoldingOnlyThatPermission()
    {
        var contributor = new TestContributor("under-declared", 1)
        {
            Overview = new() { Metrics = [new() { Id = "card", Label = "Card", Permission = InstancesView }] },
            OverviewPermissions = new() { Runtime = RuntimeView }
        };

        var overview = await CreateProvider(contributor).GetOverviewAsync(new() { CanRead = x => x == InstancesView });

        await Assert.That(contributor.Invocations).IsEqualTo(0);
        await Assert.That(overview.Metrics).IsEmpty();
    }

    public static IEnumerable<(DashboardPermission, string, string)> FailingInstanceStoreCallers() =>
    [
        (RuntimeView, DashboardCapabilityStatus.Unauthorized.Status, DashboardRuntimeStatusKeys.AcceptingWork),
        (InstancesView, DashboardCapabilityStatus.Unavailable.Status, DashboardRuntimeStatusKeys.Unavailable)
    ];

    [Test]
    [MethodDataSource(nameof(FailingInstanceStoreCallers))]
    public async Task GetOverviewAsync_WithFailingInstanceStore_ReportsReadableSectionsAvailableOrUnavailableNeverUnauthorized(
        DashboardPermission permission, string instancesStatus, string runtimeStatus)
    {
        var provider = CreateProvider(CreateWorkflowContributorWithFailingInstanceStore());

        var overview = await provider.GetOverviewAsync(new() { CanRead = x => x == permission });

        await Assert.That(overview.BackendName).IsEqualTo("Elsa.TestHost");
        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(instancesStatus);
        await Assert.That(overview.Runtime.Status).IsEqualTo(runtimeStatus);
        await Assert.That(overview.Runtime.Capability.Status).IsEqualTo(permission == RuntimeView ? DashboardCapabilityStatus.Available.Status : DashboardCapabilityStatus.Unauthorized.Status);
    }

    [Test]
    public async Task GetOverviewAsync_WithFailingInstanceStoreAndCallerWhoMayReadNothing_WithholdsEverything()
    {
        var provider = CreateProvider(CreateWorkflowContributorWithFailingInstanceStore());

        var overview = await provider.GetOverviewAsync(new() { CanRead = _ => false });

        await Assert.That(overview.BackendName).IsNull();
        await Assert.That(overview.EnvironmentName).IsNull();
        await Assert.That(overview.Runtime.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unauthorized.Status);
        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unauthorized.Status);
    }

    [Test]
    public async Task GetOverviewAsync_WithContributorThatFailsForCallerWhoMayReadItsSection_ReportsItUnavailableNotUnauthorized()
    {
        var provider = CreateProvider(new ThrowingContributor("broken", 1) { OverviewPermissions = new() { Runtime = RuntimeView, WorkflowInstances = InstancesView } });

        var overview = await provider.GetOverviewAsync(new() { CanRead = x => x == InstancesView });

        await Assert.That(overview.BackendName).IsEqualTo("Elsa.TestHost");
        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unavailable.Status);
        await Assert.That(overview.Runtime.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unauthorized.Status);
    }

    [Test]
    public async Task GetOverviewAsync_WithRuntimeOnlyCaller_DoesNotQueryTheInstanceStore()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(99L);
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowInstanceSummary>());
        var runtime = IWorkflowRuntimeAdminService.Mock().Object;
        Mock.Get(runtime).GetStatus().Returns(new RuntimeAdminStatus(QuiescenceState.Initial("generation"), [], 0));

        var overview = await CreateProvider(new WorkflowDashboardContributor(store, runtime))
            .GetOverviewAsync(new() { CanRead = x => x == RuntimeView });

        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        await Assert.That(overview.Runtime.Status).IsEqualTo(DashboardRuntimeStatusKeys.AcceptingWork);
        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unauthorized.Status);
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(0);
    }

    [Test]
    public async Task GetNeedsAttentionAsync_WithFailingInstanceStore_StillReturnsRuntimeFindings()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("secret"));
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("secret"));
        var runtime = IWorkflowRuntimeAdminService.Mock().Object;
        Mock.Get(runtime).GetStatus().Returns(new RuntimeAdminStatus(QuiescenceState.Initial("generation") with { Reason = QuiescenceReason.AdministrativePause }, [], 0));

        var response = await CreateProvider(new WorkflowDashboardContributor(store, runtime))
            .GetNeedsAttentionAsync(new() { CanRead = x => x == RuntimeView }, 10);

        await Assert.That(response.Findings.Select(x => x.Id)).IsEquivalentTo(["runtime-paused"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(response.Findings.Single().Message).DoesNotContain("secret");
    }

    [Test]
    public async Task GetOverviewAsync_WithOneInstanceContributorFailingAndAnotherSucceeding_ReportsUnavailableWithoutPartialTotals()
    {
        var provider = CreateProvider(InstanceContributor("failed", 1, new() { Capability = DashboardCapabilityStatus.Unavailable }), InstanceContributor("healthy", 2, new() { Running = 5 }));

        var overview = await provider.GetOverviewAsync(new() { CanRead = x => x == InstancesView });

        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unavailable.Status);
        await Assert.That(overview.WorkflowInstances.Capability.Reason).IsNotNull();
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(0);
    }

    [Test]
    public async Task GetOverviewAsync_WithReadableContributorFailingAndAnUnreadableOneForTheSameSection_ReportsUnavailableNotUnauthorized()
    {
        var unreadable = new TestContributor("unreadable", 2)
        {
            Overview = new() { WorkflowInstances = new() { Running = 9 } },
            OverviewPermissions = new() { WorkflowInstances = WholeOverview, Runtime = InstancesView }
        };
        var provider = CreateProvider(new ThrowingContributor("broken", 1) { OverviewPermissions = new() { WorkflowInstances = InstancesView } }, unreadable);

        var overview = await provider.GetOverviewAsync(new() { CanRead = x => x == InstancesView });

        await Assert.That(unreadable.Invocations).IsEqualTo(1);
        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unavailable.Status);
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(0);
    }

    [Test]
    public async Task GetOverviewAsync_WithOnlyUnreadableInstanceContributions_ReportsUnauthorized()
    {
        var provider = CreateProvider(InstanceContributor("failed", 1, new() { Capability = DashboardCapabilityStatus.Unavailable }), InstanceContributor("healthy", 2, new() { Running = 5 }));

        var overview = await provider.GetOverviewAsync(new() { CanRead = x => x == RuntimeView });

        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unauthorized.Status);
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(0);
    }

    [Test]
    public async Task GetOverviewAsync_WithEveryReadableInstanceContributorSucceeding_MergesTotalsAsAvailable()
    {
        var provider = CreateProvider(InstanceContributor("a", 1, new() { Running = 5 }), InstanceContributor("b", 2, new() { Running = 3 }));

        var overview = await provider.GetOverviewAsync(new() { CanRead = x => x == InstancesView });

        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Available.Status);
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(8);
    }

    private static TestContributor InstanceContributor(string id, int order, DashboardWorkflowInstanceMetrics metrics) => new(id, order)
    {
        Overview = new() { WorkflowInstances = metrics },
        OverviewPermissions = new() { WorkflowInstances = InstancesView }
    };

    private static WorkflowDashboardContributor CreateWorkflowContributorWithFailingInstanceStore()
    {
        var store = IWorkflowInstanceStore.Mock().Object;
        Mock.Get(store).CountAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Store down"));
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Store down"));
        var runtime = IWorkflowRuntimeAdminService.Mock().Object;
        Mock.Get(runtime).GetStatus().Returns(new RuntimeAdminStatus(QuiescenceState.Initial("generation"), [], 0));
        return new(store, runtime);
    }

    [Test]
    public async Task GetOverviewAsync_WithCallerWhoMayReadNothing_DoesNotInvokeContributorsThatDeclaredTheirPermissions()
    {
        var declared = Declared();
        var provider = CreateProvider(declared);

        await provider.GetOverviewAsync(new() { CanRead = _ => false });

        await Assert.That(declared.Invocations).IsEqualTo(0);
    }

    [Test]
    public async Task OtherCalls_WithCallerWhoMayReadNothing_StillInvokeContributorsThatDeclaredOverviewPermissionsAndFilterAfterwards()
    {
        var declared = Declared();
        var provider = CreateProvider(declared);
        DashboardQuery query = new() { CanRead = _ => false };

        var findings = await provider.GetNeedsAttentionAsync(query, 10);
        var trends = await provider.GetWorkflowTrendsAsync(new() { CanRead = _ => false });
        var activity = await provider.GetRecentActivityAsync(query, 10);
        var hotspots = await provider.GetWorkflowHotspotsAsync(new() { CanRead = _ => false });

        await Assert.That(declared.Invocations).IsEqualTo(4);
        await Assert.That(findings.Findings).IsEmpty();
        await Assert.That(trends.Buckets).IsEmpty();
        await Assert.That(activity.Items).IsEmpty();
        await Assert.That(hotspots.Items).IsEmpty();
    }

    [Test]
    public async Task GetOverviewAsync_SkipsOnlyTheContributorsWhoseEveryDeclaredPermissionIsUnreadable()
    {
        var runtimeOnly = new TestContributor("runtime", 2)
        {
            Overview = new() { Runtime = new() { Status = DashboardRuntimeStatusKeys.AcceptingWork } },
            OverviewPermissions = new() { Runtime = RuntimeView }
        };
        var declared = Declared();
        var provider = CreateProvider(declared, runtimeOnly);

        var overview = await provider.GetOverviewAsync(new() { CanRead = permission => permission == InstancesView });

        await Assert.That(declared.Invocations).IsEqualTo(1);
        await Assert.That(runtimeOnly.Invocations).IsEqualTo(0);
        await Assert.That(overview.Runtime.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unauthorized.Status);
    }

    [Test]
    public async Task GetOverviewAsync_WithContributorThatDeclaredNothing_StillInvokesItAndFiltersAfterwards()
    {
        var undeclared = Undeclared();
        var provider = CreateProvider(undeclared);

        var overview = await provider.GetOverviewAsync(new() { CanRead = _ => false });

        await Assert.That(undeclared.Invocations).IsEqualTo(1);
        await SectionAccess.None.AssertOn(overview);
    }

    [Test]
    public async Task GetOverviewAsync_WithCallerWhoMayReadNothing_DoesNotDiscloseWhichModulesAreInstalled()
    {
        var provider = CreateProvider(new WorkflowDashboardContributor(IWorkflowInstanceStore.Mock().Object, IWorkflowRuntimeAdminService.Mock().Object));

        var overview = await provider.GetOverviewAsync(new() { CanRead = _ => false });

        await SectionAccess.None.AssertOn(overview);
        await Assert.That(overview.BackendName).IsNull();
        await Assert.That(overview.EnvironmentName).IsNull();
    }

    [Test]
    public async Task GetOverviewAsync_WithASectionItsContributorDidNotDeclare_NeedsTheWholeOverviewPermission()
    {
        var provider = CreateProvider(new TestContributor("runtime-only", 1)
        {
            Overview = new() { Runtime = new() { Status = DashboardRuntimeStatusKeys.AcceptingWork }, WorkflowInstances = new() { Running = 3 } },
            OverviewPermissions = new() { Runtime = RuntimeView }
        });

        var overview = await provider.GetOverviewAsync(new() { CanRead = permission => permission == RuntimeView });

        await Assert.That(overview.Runtime.Status).IsEqualTo(DashboardRuntimeStatusKeys.AcceptingWork);
        await Assert.That(overview.WorkflowInstances.Capability.Status).IsEqualTo(DashboardCapabilityStatus.Unauthorized.Status);
        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(0);
    }

    [Test]
    public async Task GetNeedsAttentionAsync_WithholdsFindingsTheCallerMayNotRead()
    {
        var provider = CreateProvider(Declared());

        var response = await provider.GetNeedsAttentionAsync(new() { CanRead = permission => permission == InstancesView }, 10);

        await Assert.That(response.Findings.Select(x => x.Id)).IsEquivalentTo([InstancesFinding], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetNeedsAttentionAsync_WithFindingsThatDeclareNoPermission_NeedsTheWholeOverviewPermission()
    {
        var provider = CreateProvider(Undeclared());

        var narrow = await provider.GetNeedsAttentionAsync(new() { CanRead = permission => permission == InstancesView }, 10);
        var whole = await provider.GetNeedsAttentionAsync(new() { CanRead = permission => permission == WholeOverview }, 10);

        await Assert.That(narrow.Findings).IsEmpty();
        await Assert.That(whole.Findings).HasSingleItem();
    }

    [Test]
    public async Task GetNeedsAttentionAsync_ReturnsContributorFindingsInDeterministicOrder()
    {
        var provider = CreateProvider(
            new TestContributor("b", 20)
            {
                Findings =
                [
                    new() { Id = "second-b", Message = "Second B", Priority = 20 },
                    new() { Id = "second-a", Message = "Second A", Priority = 20 }
                ]
            },
            new TestContributor("a", 10)
            {
                Findings =
                [
                    new() { Id = "first", Message = "First", Priority = 10 }
                ]
            });

        var response = await provider.GetNeedsAttentionAsync(new(DashboardRangeKeys.TwentyFourHours), 10);
        var findings = response.Findings.ToArray();
        await Assert.That(findings).Count().IsEqualTo(3);
        await Assert.That(findings[0].Id).IsEqualTo("first");
        await Assert.That(findings[1].Id).IsEqualTo("second-a");
        await Assert.That(findings[2].Id).IsEqualTo("second-b");
    }

    [Test]
    public async Task ContributorFailure_DoesNotBreakDashboard()
    {
        var provider = CreateProvider(
            new ThrowingContributor("broken", 1),
            new TestContributor("healthy", 2)
            {
                Overview = new()
                {
                    WorkflowInstances = new()
                    {
                        Running = 7
                    }
                },
                Findings =
                [
                    new() { Id = "healthy", Message = "Healthy", Priority = 10 }
                ]
            });

        var overview = await provider.GetOverviewAsync(new(DashboardRangeKeys.TwentyFourHours));
        var needsAttention = await provider.GetNeedsAttentionAsync(new(DashboardRangeKeys.TwentyFourHours), 10);

        await Assert.That(overview.WorkflowInstances.Running).IsEqualTo(7);
        await Assert.That(needsAttention.Findings).HasSingleItem();
    }

    [Test]
    public async Task RequestCancellation_IsNotSwallowed()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var provider = CreateProvider(new CanceledContributor());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => provider.GetOverviewAsync(new(), cancellationTokenSource.Token));
    }

    [Test]
    public async Task GetWorkflowTrendsAsync_AggregatesContributorBuckets()
    {
        var from = _now.AddHours(-1);
        var provider = CreateProvider(
            new TestContributor("a", 1)
            {
                Trend = new()
                {
                    Buckets = [new() { From = from, To = _now, CreatedOrStarted = 1, Faulted = 2 }]
                }
            },
            new TestContributor("b", 2)
            {
                Trend = new()
                {
                    Buckets = [new() { From = from, To = _now, CreatedOrStarted = 3, Finished = 4 }]
                }
            });

        var response = await provider.GetWorkflowTrendsAsync(new() { Range = DashboardRangeKeys.OneHour, Granularity = DashboardTrendGranularity.Hour });
        var bucket = await Assert.That(response.Buckets).HasSingleItem();

        await Assert.That(bucket.CreatedOrStarted).IsEqualTo(4);
        await Assert.That(bucket.Finished).IsEqualTo(4);
        await Assert.That(bucket.Faulted).IsEqualTo(2);
    }

    [Test]
    public async Task GetRecentActivityAsync_MergesAndLimitsContributorItems()
    {
        var provider = CreateProvider(
            new TestContributor("a", 1)
            {
                RecentActivity = new()
                {
                    Items = [Recent("old", _now.AddMinutes(-10)), Recent("new", _now)]
                }
            },
            new TestContributor("b", 2)
            {
                RecentActivity = new()
                {
                    Items = [Recent("middle", _now.AddMinutes(-5))]
                }
            });

        var response = await provider.GetRecentActivityAsync(new(DashboardRangeKeys.OneHour), 2);
        var items = response.Items.ToArray();
        await Assert.That(items).Count().IsEqualTo(2);
        await Assert.That(items[0].InstanceId).IsEqualTo("new");
        await Assert.That(items[1].InstanceId).IsEqualTo("middle");
    }

    [Test]
    public async Task GetWorkflowTrendsAsync_WithholdsContributionsTheCallerMayNotRead()
    {
        var provider = CreateProvider(Declared(), Undeclared());

        var narrow = await provider.GetWorkflowTrendsAsync(new() { CanRead = permission => permission == InstancesView });
        var whole = await provider.GetWorkflowTrendsAsync(new() { CanRead = _ => true });

        await Assert.That(narrow.Buckets.Select(x => x.CreatedOrStarted)).IsEquivalentTo([1L], EqualityComparer<long>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(whole.Buckets.Select(x => x.CreatedOrStarted)).IsEquivalentTo([11L], EqualityComparer<long>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetRecentActivityAsync_WithholdsContributionsTheCallerMayNotRead()
    {
        var provider = CreateProvider(Declared(), Undeclared());

        var narrow = await provider.GetRecentActivityAsync(new() { CanRead = permission => permission == InstancesView }, 10);
        var whole = await provider.GetRecentActivityAsync(new() { CanRead = _ => true }, 10);

        await Assert.That(narrow.Items.Select(x => x.InstanceId)).IsEquivalentTo(["instance"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(whole.Items.Select(x => x.InstanceId).Order()).IsEquivalentTo(["instance", "undeclared"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetWorkflowHotspotsAsync_WithholdsContributionsTheCallerMayNotRead()
    {
        var provider = CreateProvider(Declared(), Undeclared());

        var narrow = await provider.GetWorkflowHotspotsAsync(new() { CanRead = permission => permission == InstancesView });
        var whole = await provider.GetWorkflowHotspotsAsync(new() { CanRead = _ => true });

        await Assert.That(narrow.Items.Select(x => x.DefinitionId)).IsEquivalentTo(["definition"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(whole.Items.Select(x => x.DefinitionId).Order()).IsEquivalentTo(["definition", "undeclared"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task DashboardApiProject_DoesNotReferenceWorkflowOrDiagnosticsModules()
    {
        var projectFile = FindRepositoryRoot().Combine("src/modules/Elsa.Dashboard.Api/Elsa.Dashboard.Api.csproj");
        var project = File.ReadAllText(projectFile);

        await Assert.That(project).DoesNotContain("Elsa.Workflows");
        await Assert.That(project).DoesNotContain("Elsa.Diagnostics");
        await Assert.That(project).Contains("Elsa.Dashboard.Abstractions");
    }

    [Test]
    public async Task OwnerProjects_DoNotReferenceDashboardAbstractions()
    {
        var root = FindRepositoryRoot();
        var ownerProjects = new[]
        {
            "src/modules/Elsa.Workflows.Runtime/Elsa.Workflows.Runtime.csproj"
        };

        foreach (var ownerProject in ownerProjects)
        {
            var project = File.ReadAllText(root.Combine(ownerProject));
            await Assert.That(project).DoesNotContain("Elsa.Dashboard.Abstractions");
        }
    }

    [Test]
    public async Task DashboardCompanionProjects_RegisterExpectedContributors()
    {
        var services = new ServiceCollection();

        services.AddWorkflowRuntimeDashboard();

        await AssertRegisteredContributor<WorkflowDashboardContributor>(services);
        await Assert.That(services.Count(x => x.ServiceType == typeof(IDashboardContributor))).IsEqualTo(1);
    }

    private DefaultDashboardProvider CreateProvider(params IDashboardContributor[] contributors) =>
        new(contributors, new(new FakeTimeProvider(_now)), new TestHostEnvironment());

    private static async Task AssertRegisteredContributor<TContributor>(IServiceCollection services)
    {
        await Assert.That(services).Contains(descriptor =>
            descriptor.ServiceType == typeof(IDashboardContributor) &&
            descriptor.ImplementationType == typeof(TContributor) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private static DashboardRecentActivityItem Recent(string id, DateTimeOffset updatedAt) => new()
    {
        InstanceId = id,
        DefinitionId = "workflow",
        Status = "Finished",
        SubStatus = "Finished",
        CreatedAt = updatedAt.AddMinutes(-1),
        UpdatedAt = updatedAt
    };

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.slnx")))
                return directory;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class ThrowingContributor(string id, int order) : IDashboardContributor
    {
        public string Id { get; } = id;

        public DashboardOverviewPermissions? OverviewPermissions { get; init; }

        public int Order { get; } = order;

        public ValueTask<DashboardOverviewContribution?> GetOverviewAsync(DashboardContext context) => throw new InvalidOperationException("Broken");

        public ValueTask<IReadOnlyCollection<DashboardFinding>> GetFindingsAsync(DashboardContext context) => throw new InvalidOperationException("Broken");
    }

    private sealed class CanceledContributor : IDashboardContributor
    {
        public string Id => "canceled";

        public int Order => 0;

        public ValueTask<DashboardOverviewContribution?> GetOverviewAsync(DashboardContext context) => throw new OperationCanceledException(context.CancellationToken);
    }
}

internal static class DirectoryInfoExtensions
{
    public static string Combine(this DirectoryInfo directory, string path) => Path.Combine(directory.FullName, path);
}
