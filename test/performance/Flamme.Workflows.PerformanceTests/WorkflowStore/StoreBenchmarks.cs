using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using Flamme.Common;
using Flamme.Extensions;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Tenants.Extensions;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Mappers;
using Flamme.Workflows.Runtime.Stimuli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

/// <summary>
/// Measures one committed update of existing instances, or serialization of the same suspended workflow state.
/// Time and allocations are per call, including all rows in a batch.
/// </summary>
[Config(typeof(Config)), JsonExporterAttribute.Full, GcServer(true)]
public class StoreBenchmarks
{
    private PostgreSqlContainer? _postgres;
    private IHost? _host;
    private IServiceScope? _scope;
    private IWorkflowInstanceStore _store = null!;
    private IWorkflowStateSerializer _serializer = null!;
    private ICompressionCodecResolver _codecs = null!;
    private WorkflowInstance[][] _revisions = null!;
    private int _revision;
    private bool _serializeOnly;
    private string? _serialized;

    [GlobalSetup(Targets = [nameof(SaveSingle), nameof(SaveBatch1)])]
    public Task SetupSingle() => SetupAsync(1);

    [GlobalSetup(Target = nameof(SaveBatch50))]
    public Task SetupBatch50() => SetupAsync(50);

    [GlobalSetup(Target = nameof(SaveBatch500))]
    public Task SetupBatch500() => SetupAsync(500);

    [GlobalSetup(Target = nameof(SerializeOne))]
    public Task SetupSerializer() => SetupAsync(1, serializeOnly: true);

    [Benchmark(Baseline = true)]
    public Task SaveSingle() => _store.SaveAsync(NextRevision()[0]).AsTask();

    [Benchmark]
    public Task SaveBatch1() => _store.SaveManyAsync(NextRevision()).AsTask();

    [Benchmark]
    public Task SaveBatch50() => _store.SaveManyAsync(NextRevision()).AsTask();

    [Benchmark]
    public Task SaveBatch500() => _store.SaveManyAsync(NextRevision()).AsTask();

    [Benchmark]
    public async Task SerializeOne()
    {
        var json = _serializer.Serialize(NextRevision()[0].WorkflowState);
        _serialized = await _codecs.Resolve("None").CompressAsync(json);
    }

    private WorkflowInstance[] NextRevision()
    {
        // Revision zero is the seed. Alternate two detached revisions so each save changes the row.
        _revision = _revision == 1 ? 2 : 1;
        return _revisions[_revision];
    }

    private async Task SetupAsync(int count, bool serializeOnly = false)
    {
        _serializeOnly = serializeOnly;
        _postgres = WorkflowPostgres.Create();
        try
        {
            await _postgres.StartAsync();
            var connectionString = _postgres.GetConnectionString();
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddFlamme(f =>
            {
                f.UseTenants();
                f.UseWorkflowRuntime();
                f.UseWorkflowManagement(m => m.UseWorkflowInstances(i =>
                    i.UseEntityFrameworkCore(p => p.UsePostgreSql(connectionString))));
            });

            _host = builder.Build();
            _scope = _host.Services.CreateScope();
            var services = _scope.ServiceProvider;
            _store = services.GetRequiredService<IWorkflowInstanceStore>();
            _serializer = services.GetRequiredService<IWorkflowStateSerializer>();
            _codecs = services.GetRequiredService<ICompressionCodecResolver>();
            var mapper = services.GetRequiredService<WorkflowStateMapper>();
            var contextFactory = services.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>();
            await using var db = await contextFactory.CreateDbContextAsync();
            await db.Database.MigrateAsync();

            using var stream = typeof(StoreBenchmarks).Assembly.GetManifestResourceStream(typeof(StoreBenchmarks), "SuspendedWorkflowState.json")
                ?? throw new InvalidOperationException("Missing suspended workflow state resource.");
            using var reader = new StreamReader(stream);
            var fixture = await reader.ReadToEndAsync();
            var original = _serializer.Deserialize(fixture);
            if (original.Bookmarks.Single().Payload is not EventStimulus { EventName: "store-bench" } || _serializer.Serialize(original) != fixture)
                throw new InvalidOperationException("The captured event checkpoint did not round-trip through Flamme.");

            _revisions = [MapRevision(0), MapRevision(1), MapRevision(2)];

            WorkflowInstance[] MapRevision(int revision)
            {
                var rows = new WorkflowInstance[count];
                for (var i = 0; i < rows.Length; i++)
                {
                    var state = _serializer.Deserialize(fixture);
                    state.Id = $"{i:x16}";
                    state.CorrelationId = $"bdn-fixture-synthetic-{revision}";
                    rows[i] = mapper.Map(state)!;
                }

                return rows;
            }

            await _store.SaveManyAsync(_revisions[0]);
        }
        catch
        {
            await DisposeFixtureAsync();
            throw;
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        try
        {
            if (_host is null)
                return;
            if (_revision == 0)
                throw new InvalidOperationException("The benchmark never ran.");
            if (_serializeOnly && _serialized != _serializer.Serialize(_revisions[_revision][0].WorkflowState))
                throw new InvalidOperationException("The serialized checkpoint differs from the expected revision.");

            // The serializer control must leave the seed untouched. Each save must persist the last revision.
            var expected = _revisions[_serializeOnly ? 0 : _revision].ToDictionary(row => row.Id);
            using var audit = _host.Services.CreateScope();
            var store = audit.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
            var restored = (await store.FindManyAsync(new() { Ids = expected.Keys.ToArray() })).ToArray();
            if (restored.Length != expected.Count || restored.Any(row =>
                row.TenantId != "" || row.WorkflowState.Id != row.Id || row.WorkflowState.CorrelationId != expected[row.Id].CorrelationId))
                throw new InvalidOperationException("A fresh store read did not restore the expected workflow instances.");

            var factory = audit.ServiceProvider.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var persisted = await db.WorkflowInstances.IgnoreQueryFilters().Select(row => new
            {
                row.Id,
                row.TenantId,
                row.CorrelationId,
                Codec = EF.Property<string>(row, "DataCompressionAlgorithm"),
                Data = EF.Property<string>(row, "Data")
            }).ToListAsync();
            if (persisted.Count != expected.Count || persisted.Any(row =>
                row.TenantId != "" || row.Codec != "None" || row.CorrelationId != expected[row.Id].CorrelationId
                || row.Data != _serializer.Serialize(expected[row.Id].WorkflowState)))
                throw new InvalidOperationException("The persisted checkpoint or codec differs from the submitted revision.");
        }
        finally
        {
            await DisposeFixtureAsync();
        }
    }

    private async Task DisposeFixtureAsync()
    {
        // Release the scope and host before stopping PostgreSQL, including when setup or verification fails.
        await using var postgres = _postgres;
        using var host = _host;
        using var scope = _scope;
        _scope = null;
        _host = null;
        _postgres = null;
    }
}
