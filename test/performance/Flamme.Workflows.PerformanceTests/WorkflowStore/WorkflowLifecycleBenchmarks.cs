using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

/// <summary>
/// Build a suspended backlog with 48 callers, then drain the same IDs with 80 callers.
/// Set FLAMME_BENCH_WORKFLOW_COUNT to "default" (256) or a positive count to opt in.
/// For a million, use --job Dry --filter '*WorkflowLifecycleBenchmarks*' for a single campaign.
/// BDN measures the resume phase. Iteration cleanup reports both HTTP phase durations.
/// </summary>
[Config(typeof(OptInConfig)), RunOncePerIteration, EvaluateOverhead(false)]
public class WorkflowLifecycleBenchmarks
{
    private const string CountVariable = "FLAMME_BENCH_WORKFLOW_COUNT";
    private const int StartConcurrency = 48;
    private const int ResumeConcurrency = 80;
    private WorkflowHostFixture _host = null!;
    private WorkflowHttpClient _api = null!;
    private WorkflowHttpObserver _observer = null!;
    private WorkflowHttpInstance[] _instances = [];
    private int _count;
    private string _definition = "";
    private TimeSpan _startTime;
    private TimeSpan _resumeTime;

    public sealed class OptInConfig : ManualConfig
    {
        public OptInConfig()
        {
            var raw = Environment.GetEnvironmentVariable(CountVariable);
            if (raw == null)
                AddFilter(new SimpleFilter(c => c.Descriptor.Type != typeof(WorkflowLifecycleBenchmarks)));
            else
                AddJob(Job.Default.WithEnvironmentVariable(CountVariable, raw).AsMutator());
        }
    }

    private static int ReadCount()
    {
        var raw = Environment.GetEnvironmentVariable(CountVariable);
        if (raw == "default")
            return 256;
        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0)
            return count;
        throw new ArgumentException($"Set {CountVariable} to default or a positive integer.");
    }

    [GlobalSetup]
    public async Task StartHost()
    {
        _count = ReadCount();
        _host = await WorkflowHostFixture.StartAsync();
        _api = new WorkflowHttpClient(_host.Client);
        _observer = new WorkflowHttpObserver(_host.ConnectionString);
    }

    [IterationSetup]
    public void PrepareSuspendedBacklog() => StartAndVerifyAsync().GetAwaiter().GetResult();

    private async Task StartAndVerifyAsync()
    {
        _definition = $"lifecycle-{Guid.NewGuid():N}";
        await _api.PublishAsync(_definition);
        _instances = new WorkflowHttpInstance[_count];
        var started = Stopwatch.GetTimestamp();
        await Parallel.ForAsync(0, _count, new ParallelOptions { MaxDegreeOfParallelism = StartConcurrency },
            async (i, _) => { _instances[i] = await _api.StartAsync(_definition, $"{_definition}-{i}"); });
        _startTime = Stopwatch.GetElapsedTime(started);
        await VerifyAsync(finished: false);
    }

    [Benchmark]
    public async Task ResumeSuspendedBacklog()
    {
        var started = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(_instances, new ParallelOptions { MaxDegreeOfParallelism = ResumeConcurrency },
            async (instance, _) => await _api.ResumeAsync(instance));
        _resumeTime = Stopwatch.GetElapsedTime(started);
    }

    [IterationCleanup]
    public void VerifyFinishedBacklog()
    {
        VerifyAsync(finished: true).GetAwaiter().GetResult();
        Console.WriteLine(FormattableString.Invariant(
            $"Lifecycle {_definition} started {_count} workflows with up to {StartConcurrency} callers in {_startTime.TotalSeconds:F3} s."));
        Console.WriteLine(FormattableString.Invariant(
            $"Lifecycle {_definition} resumed {_count} workflows with up to {ResumeConcurrency} callers in {_resumeTime.TotalSeconds:F3} s. Both durable audits passed."));
    }

    private async Task VerifyAsync(bool finished)
    {
        foreach (var batch in _instances.Chunk(1024))
        {
            int matched = await _observer.CountMatchingAsync(_definition, batch, finished);
            if (matched != batch.Length)
                throw new InvalidDataException($"Expected {batch.Length} {(finished ? "finished" : "suspended")} instances in the audit batch, verified {matched}.");
        }
    }

    [GlobalCleanup]
    public async Task StopHost()
    {
        if (_host != null) await _host.DisposeAsync();
    }
}
