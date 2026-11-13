using Elsa.Dashboard.Abstractions.Models;

namespace Elsa.Dashboard.Api.UnitTests;

/// <summary>Which overview sections a caller reads: a section is either readable, with its data, or withheld, without it.</summary>
public record SectionAccess
{
    public static readonly SectionAccess None = new();
    public static readonly SectionAccess All = new() { Runtime = true, Instances = true, StructuredLogs = true, ConsoleLogs = true };

    public bool Runtime { get; init; }
    public bool Instances { get; init; }
    public bool StructuredLogs { get; init; }
    public bool ConsoleLogs { get; init; }

    /// <summary>The ids of the metric cards and panels built from the readable data, where the caller holds only that permission.</summary>
    public string[] Metrics { get; init; } = [];
    public string[] Panels { get; init; } = [];

    /// <summary>What a caller holding exactly one section's permission reads, for contributors supplying <see cref="TestContributor.Declared"/> data.</summary>
    public static readonly IReadOnlyList<(DashboardPermission Permission, SectionAccess Readable)> BySinglePermission =
    [
        (TestContributor.RuntimeView, new() { Runtime = true, Metrics = ["runtime"] }),
        (TestContributor.InstancesView, new() { Instances = true, Metrics = [TestContributor.InstancesMetric], Panels = [TestContributor.InstancesPanel] }),
        (TestContributor.StructuredLogsView, new() { StructuredLogs = true, Panels = [TestContributor.LogsPanel] }),
        (TestContributor.ConsoleLogsView, new() { ConsoleLogs = true })
    ];

    /// <summary>Asserts the overview of a caller against the sections it may read, for contributors supplying <see cref="TestContributor.Declared"/> data.</summary>
    public async Task AssertOn(DashboardOverview overview)
    {
        await AssertSection(Runtime, overview.Runtime.Capability, overview.Runtime.Status == DashboardRuntimeStatusKeys.AcceptingWork);
        await AssertSection(Instances, overview.WorkflowInstances.Capability, overview.WorkflowInstances.Running == 3);
        await AssertSection(StructuredLogs, overview.Diagnostics.StructuredLogs.Capability, overview.Diagnostics.StructuredLogs.SourceCount == 2);
        await AssertSection(ConsoleLogs, overview.Diagnostics.ConsoleLogs.Capability, overview.Diagnostics.ConsoleLogs.SourceCount == 4);
    }

    private static async Task AssertSection(bool readable, DashboardCapabilityStatus capability, bool hasData)
    {
        await Assert.That(capability.Status != DashboardCapabilityStatus.Unauthorized.Status).IsEqualTo(readable);
        await Assert.That(hasData).IsEqualTo(readable);
    }
}
