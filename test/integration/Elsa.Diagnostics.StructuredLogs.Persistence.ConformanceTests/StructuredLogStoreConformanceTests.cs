using Elsa.Diagnostics.StructuredLogs.Models;
using Elsa.Diagnostics.StructuredLogs.Options;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.ConformanceTests;

/// <summary>
/// Shared InMemory / Relational store-contract assertions for <c>IStructuredLogStore</c>.
/// Locks the Take clamp, SourceId tie-break, ListSources registry/heartbeat, DroppedEvents,
/// and portable filter contracts aligned after #8147/#8148.
/// </summary>
/// <remarks>
/// Equality filters that differ only by case are not a shared contract: InMemory uses
/// <see cref="StringComparison.OrdinalIgnoreCase"/> while Relational SQL uses <c>=</c>
/// (ordinal for the in-memory store). Tests use exact-case values. See README.md.
/// </remarks>
public abstract class StructuredLogStoreConformanceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    protected abstract Task<StructuredLogStoreScenario> CreateScenarioAsync(Action<StructuredLogsOptions>? configure = null);

    [Test]
    public async Task QueryAsync_WhenTakeIsNull_UsesMaxRecentLogQuerySize()
    {
        await using var scenario = await CreateScenarioAsync(options => options.MaxRecentLogQuerySize = 2);
        await scenario.WriteAsync(
            Log("one", BaseTime, sequence: 1),
            Log("two", BaseTime.AddMinutes(1), sequence: 2),
            Log("three", BaseTime.AddMinutes(2), sequence: 3),
            Log("four", BaseTime.AddMinutes(3), sequence: 4));

        var result = await scenario.Store.QueryAsync(new());

        await Assert.That(result.Items.Select(x => x.Id).ToList()).IsEquivalentTo(["three", "four"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_WhenTakeExceedsMax_ClampsToMaxRecentLogQuerySize()
    {
        await using var scenario = await CreateScenarioAsync(options => options.MaxRecentLogQuerySize = 2);
        await scenario.WriteAsync(
            Log("one", BaseTime, sequence: 1),
            Log("two", BaseTime.AddMinutes(1), sequence: 2),
            Log("three", BaseTime.AddMinutes(2), sequence: 3));

        var result = await scenario.Store.QueryAsync(new() { Take = 100 });

        await Assert.That(result.Items.Select(x => x.Id).ToList()).IsEquivalentTo(["two", "three"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_WhenTakeIsNegative_ReturnsEmpty()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.WriteAsync(Log("one", BaseTime));

        var result = await scenario.Store.QueryAsync(new() { Take = -5 });

        await Assert.That(result.Items).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_WhenMaxRecentLogQuerySizeIsNegative_ReturnsEmpty()
    {
        await using var scenario = await CreateScenarioAsync(options => options.MaxRecentLogQuerySize = -10);
        await scenario.WriteAsync(Log("one", BaseTime));

        var result = await scenario.Store.QueryAsync(new() { Take = 25 });

        await Assert.That(result.Items).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_WhenTimestampsTie_OrdersBySourceId()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.WriteAsync(
            Log("zzz", BaseTime, sourceId: "pod-b", sequence: 1),
            Log("aaa", BaseTime, sourceId: "pod-a", sequence: 2));

        var result = await scenario.Store.QueryAsync(new() { Take = 10 });

        await Assert.That(result.Items.Select(x => x.SourceId).ToList()).IsEquivalentTo(["pod-a", "pod-b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_WhenSourceIdTies_OrdersBySequence()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.WriteAsync(
            Log("aaa", BaseTime, sourceId: "pod-a", sequence: 9),
            Log("zzz", BaseTime, sourceId: "pod-a", sequence: 2));

        var result = await scenario.Store.QueryAsync(new() { Take = 10 });

        await Assert.That(result.Items.Select(x => x.Id).ToList()).IsEquivalentTo(["zzz", "aaa"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_WhenSequenceTies_OrdersById()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.WriteAsync(
            Log("zzz", BaseTime, sourceId: "pod-a", sequence: 1),
            Log("aaa", BaseTime, sourceId: "pod-a", sequence: 1));

        var result = await scenario.Store.QueryAsync(new() { Take = 10 });

        await Assert.That(result.Items.Select(x => x.Id).ToList()).IsEquivalentTo(["aaa", "zzz"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_AppliesExactEqualityFiltersAndTimeRangeBeforeTake()
    {
        await using var scenario = await CreateScenarioAsync();
        var target = Log(
            "filter-target",
            BaseTime.AddMinutes(2),
            StructuredLogLevel.Warning,
            category: "Elsa.Workflow.Target",
            sourceId: "source-target",
            sequence: 2,
            message: "needle in message",
            tenantId: "tenant-a",
            workflowDefinitionId: "wf-def",
            workflowInstanceId: "wf-inst",
            correlationId: "corr-target",
            traceId: "trace-target",
            spanId: "span-target");
        await scenario.WriteAsync(
            Log("distractor", BaseTime, StructuredLogLevel.Debug, sourceId: "source-other", sequence: 1),
            target,
            Log("recent", BaseTime.AddDays(1), StructuredLogLevel.Error, sequence: 3));

        await AssertMatchesAsync(scenario, new() { Levels = [StructuredLogLevel.Warning] });
        await AssertMatchesAsync(scenario, new() { MinimumLevel = StructuredLogLevel.Warning, To = BaseTime.AddHours(1) });
        await AssertMatchesAsync(scenario, new() { CategoryPrefix = "Elsa.Workflow" });
        await AssertMatchesAsync(scenario, new() { SourceId = "source-target" });
        await AssertMatchesAsync(scenario, new() { TenantId = "tenant-a" });
        await AssertMatchesAsync(scenario, new() { WorkflowDefinitionId = "wf-def" });
        await AssertMatchesAsync(scenario, new() { WorkflowInstanceId = "wf-inst" });
        await AssertMatchesAsync(scenario, new() { CorrelationId = "corr-target" });
        await AssertMatchesAsync(scenario, new() { TraceId = "trace-target" });
        await AssertMatchesAsync(scenario, new() { SpanId = "span-target" });
        await AssertMatchesAsync(scenario, new() { Text = "needle" });
        await AssertMatchesAsync(scenario, new()
        {
            From = BaseTime.AddMinutes(1),
            To = BaseTime.AddMinutes(3)
        });
    }

    [Test]
    public async Task ListSourcesAsync_ExposesRegistryMetadataAndHeartbeat()
    {
        await using var scenario = await CreateScenarioAsync(options =>
            options.SourceHeartbeatTimeout = TimeSpan.FromSeconds(5));
        var current = scenario.SourceRegistry.Current;
        await scenario.WriteAsync(Log("local-1", DateTimeOffset.UtcNow, sourceId: current.Id, sequence: 1));
        scenario.SourceRegistry.MarkSeen("stale-pod", DateTimeOffset.UtcNow.AddMinutes(-1));
        await scenario.WriteAsync(Log("fresh-1", DateTimeOffset.UtcNow, sourceId: "fresh-pod", sequence: 2));

        var sources = await scenario.Store.ListSourcesAsync();
        var currentSource = (await Assert.That(sources.Where(x => x.Id == current.Id)).HasSingleItem());
        var staleSource = (await Assert.That(sources.Where(x => x.Id == "stale-pod")).HasSingleItem());
        var freshSource = (await Assert.That(sources.Where(x => x.Id == "fresh-pod")).HasSingleItem());

        await Assert.That(currentSource.DisplayName).IsEqualTo(current.DisplayName);
        await Assert.That(currentSource.ServiceName).IsEqualTo(current.ServiceName);
        await Assert.That(currentSource.ProcessId).IsEqualTo(current.ProcessId);
        await Assert.That(currentSource.ProcessId).IsNotEqualTo(0);
        await Assert.That(currentSource.Status).IsEqualTo(StructuredLogSourceStatus.Connected);
        await Assert.That(staleSource.Status).IsEqualTo(StructuredLogSourceStatus.Stale);
        await Assert.That(freshSource.Status).IsEqualTo(StructuredLogSourceStatus.Connected);
    }

    [Test]
    public async Task QueryAsync_ReportsDroppedEventsPerStoreContract()
    {
        await using var scenario = await CreateScenarioAsync(options => options.RecentLogCapacity = 2);
        await scenario.WriteAsync(
            Log("one", BaseTime, sequence: 1),
            Log("two", BaseTime.AddMinutes(1), sequence: 2),
            Log("three", BaseTime.AddMinutes(2), sequence: 3),
            Log("four", BaseTime.AddMinutes(3), sequence: 4));

        var result = await scenario.Store.QueryAsync(new() { Take = 10 });

        await Assert.That(result.DroppedEvents >= 0).IsTrue();
        if (scenario.ReportsRingDroppedEvents)
        {
            await Assert.That(result.DroppedEvents).IsEqualTo(2);
        }
        else
        {
            await Assert.That(result.DroppedEvents).IsEqualTo(0);
        }
    }

    [Test]
    public async Task QueryAsync_WhenWritesFit_ReportsZeroDroppedEvents()
    {
        await using var scenario = await CreateScenarioAsync();
        await scenario.WriteAsync(
            Log("one", BaseTime, sequence: 1),
            Log("two", BaseTime.AddMinutes(1), sequence: 2));

        var result = await scenario.Store.QueryAsync(new() { Take = 10 });

        await Assert.That(result.DroppedEvents).IsEqualTo(0);
        await Assert.That(result.Items.Count).IsEqualTo(2);
    }

    private static async Task AssertMatchesAsync(StructuredLogStoreScenario scenario, StructuredLogFilter filter)
    {
        var result = await scenario.Store.QueryAsync(filter with { Take = 1 });
        var item = (await Assert.That(result.Items).HasSingleItem());
        await Assert.That(item.Id).IsEqualTo("filter-target");
    }

    private static StructuredLogEvent Log(
        string id,
        DateTimeOffset timestamp,
        StructuredLogLevel level = StructuredLogLevel.Information,
        string category = "Elsa.Tests",
        string sourceId = "source-a",
        long sequence = 1,
        string message = "Test event",
        string? tenantId = null,
        string? workflowDefinitionId = null,
        string? workflowInstanceId = null,
        string? correlationId = null,
        string? traceId = null,
        string? spanId = null) =>
        new()
        {
            Id = id,
            Sequence = sequence,
            Timestamp = timestamp,
            ReceivedAt = timestamp,
            Level = level,
            Category = category,
            Message = message,
            MessageTemplate = message,
            SourceId = sourceId,
            TenantId = tenantId,
            WorkflowDefinitionId = workflowDefinitionId,
            WorkflowInstanceId = workflowInstanceId,
            CorrelationId = correlationId,
            TraceId = traceId,
            SpanId = spanId
        };
}

[InheritsTests]
public sealed class InMemoryStructuredLogStoreConformanceTests : StructuredLogStoreConformanceTests
{
    protected override Task<StructuredLogStoreScenario> CreateScenarioAsync(Action<StructuredLogsOptions>? configure = null) =>
        StructuredLogStoreScenario.CreateInMemoryAsync(configure);
}
