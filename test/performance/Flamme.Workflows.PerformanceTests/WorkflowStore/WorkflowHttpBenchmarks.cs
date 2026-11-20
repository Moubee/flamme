using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using Npgsql;
using System.Diagnostics;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

public enum WorkflowHttpOperation { Resume, Import }

/// <summary>Time a batch of 48 unrelated, existing-instance writes through the reference server.</summary>
[JsonExporterAttribute.Full, GcServer(true), RunOncePerIteration]
[EvaluateOverhead(false)]
public class WorkflowHttpBenchmarks
{
    private const int PeerCount = 48;
    private readonly string _definition = $"store-bench-{Guid.NewGuid():N}";
    private readonly WorkflowHttpInstance[] _instances = new WorkflowHttpInstance[PeerCount + 1];
    private readonly byte[][] _imports = new byte[PeerCount + 1][];
    private readonly double[] _peerLatency = new double[PeerCount];
    private WorkflowHostFixture _host = null!;
    private WorkflowHttpClient _api = null!;
    private WorkflowHttpObserver _observer = null!;
    private NpgsqlConnection _holder = null!;
    private NpgsqlTransaction? _lock;
    private (string Id, string Version) _heldRow;
    private long _writerObservedAt;
    private Task _anchor = Task.CompletedTask;
    private Task _release = Task.CompletedTask;

    [ParamsAllValues] public WorkflowHttpOperation Operation { get; set; }
    [Params(false, true)] public bool HoldRow { get; set; }

    [GlobalSetup]
    public async Task StartHost()
    {
        _host = await WorkflowHostFixture.StartAsync();
        try
        {
            _api = new WorkflowHttpClient(_host.Client);
            _observer = new WorkflowHttpObserver(_host.ConnectionString);
            _holder = new NpgsqlConnection(_host.ConnectionString);
            await _holder.OpenAsync();
            await _api.PublishAsync(_definition);
        }
        catch
        {
            await StopHost();
            throw;
        }
    }

    [IterationSetup]
    public void PrepareIteration() => PrepareAsync().GetAwaiter().GetResult();

    private async Task PrepareAsync()
    {
        _release = Task.CompletedTask;
        bool import = Operation == WorkflowHttpOperation.Import;
        await Parallel.ForAsync(0, _instances.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, _) =>
        {
            var instance = await _api.StartAsync(_definition, Guid.NewGuid().ToString("N"));
            _instances[i] = instance;
            if (import)
            {
                await _api.ResumeAsync(instance);
                _imports[i] = await _api.PrepareImportAsync(instance, instance.Correlation + "-imported");
            }
        });
        await VerifyAllAsync(finished: import);
        if (import)
            for (int i = 0; i < _instances.Length; i++)
                _instances[i] = _instances[i] with { Correlation = _instances[i].Correlation + "-imported" };

        if (!HoldRow) return;
        _lock = await _holder.BeginTransactionAsync();
        try
        {
            _heldRow = await WorkflowHttpObserver.LockAsync(_holder, _lock, _instances[0].Id);
            _anchor = SaveAsync(0);
            await using var observer = new NpgsqlConnection(_host.ConnectionString);
            await observer.OpenAsync();
            await WorkflowHttpObserver.WaitForWriterAsync(observer, _holder.ProcessID, _anchor);
            _writerObservedAt = Stopwatch.GetTimestamp();
        }
        catch
        {
            await UnlockAsync();
            await _anchor;
            throw;
        }
    }

    [Benchmark]
    public Task SaveUnrelatedInstances()
    {
        // The peer batch is timed. Cleanup waits out the rest of the one-second hold.
        var deadline = HoldRow ? Task.Delay(TimeSpan.FromSeconds(1)) : Task.CompletedTask;
        if (!HoldRow) _anchor = SaveAsync(0);
        var peers = Enumerable.Range(1, PeerCount).Select(SavePeerAsync).ToArray();
        if (HoldRow) _release = ObserveBeforeReleaseAsync(deadline, peers);
        return Task.WhenAll(peers);
    }

    private Task SaveAsync(int i) => Operation == WorkflowHttpOperation.Resume
        ? _api.ResumeAsync(_instances[i]) : _api.ImportAsync(_imports[i]);

    private async Task SavePeerAsync(int i)
    {
        var started = Stopwatch.GetTimestamp();
        await SaveAsync(i);
        _peerLatency[i - 1] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private async Task ObserveBeforeReleaseAsync(Task deadline, Task[] peers)
    {
        try
        {
            await deadline;
            int durable = await _observer.CountMatchingAsync(_definition, _instances[1..], finished: true, heldRow: _heldRow);
            int completed = peers.Count(peer => peer.IsCompletedSuccessfully);
            if (_anchor.IsCompleted)
                throw new InvalidOperationException("The anchor request completed before its row was released.");
            var holdMs = Stopwatch.GetElapsedTime(_writerObservedAt).TotalMilliseconds;
            Console.WriteLine($"Before row release: {durable}/{PeerCount} unrelated commits, {completed}/{PeerCount} successful HTTP responses, {holdMs:F1} ms since the writer was observed waiting.");
        }
        finally
        {
            await UnlockAsync();
        }
    }

    private async Task UnlockAsync()
    {
        if (_lock is null) return;
        await _lock.DisposeAsync(); // Roll back the observer's SELECT FOR UPDATE.
        _lock = null;
    }

    [IterationCleanup]
    public void AuditIteration() => AuditAsync().GetAwaiter().GetResult();

    private async Task AuditAsync()
    {
        await Task.WhenAll(_release, _anchor);
        await VerifyAllAsync(finished: true);
        Array.Sort(_peerLatency);
        double Percentile(double rank) => _peerLatency[(int)Math.Ceiling(PeerCount * rank) - 1];
        Console.WriteLine($"Peer latency (ms): p50 {Percentile(.50):F2}, p95 {Percentile(.95):F2}, p99 {Percentile(.99):F2}.");
    }

    private async Task VerifyAllAsync(bool finished)
    {
        int matched = await _observer.CountMatchingAsync(_definition, _instances, finished);
        if (matched != _instances.Length)
            throw new InvalidOperationException($"Expected {_instances.Length} committed workflow states and bookmark outcomes, verified {matched}.");
    }

    [GlobalCleanup]
    public async Task StopHost()
    {
        try
        {
            if (_holder != null) await _holder.DisposeAsync();
        }
        finally
        {
            if (_host != null) await _host.DisposeAsync();
        }
    }
}
