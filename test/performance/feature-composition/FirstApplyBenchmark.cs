using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Elsa.Features.Contracts;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.FeatureComposition.Benchmarks;

[Config(typeof(ColdCompositionConfig))]
public class FirstApplyBenchmark : CompositionBenchmark
{
    private Func<IModule> _compose = null!;
    private IModule? _module;
    private long _bytes;

    [GlobalSetup]
    public void Setup() => _compose = Prepare();

    [Benchmark]
    public void FirstApply()
    {
        if (_module != null) throw new InvalidOperationException("First Apply requires a fresh process.");
        var before = GC.GetTotalAllocatedBytes(precise: true);
        _module = _compose();
        _bytes = GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    [GlobalCleanup]
    public void ReportFirstInvocation()
    {
        var module = _module ?? throw new InvalidOperationException("No composition was measured.");
        var mode = CheckModule(module);
        using var provider = module.Services.BuildServiceProvider();
        var features = provider.GetRequiredService<IInstalledFeatureRegistry>().List().Select(f => f.FullName).Order().ToArray();
        var directory = Environment.GetEnvironmentVariable("ELSA_STARTUP_SAMPLES")!;
        var sample = new StartupSample(Graph, mode, _bytes, features);
        File.WriteAllText(Path.Combine(directory, $"{Environment.ProcessId}.json"), JsonSerializer.Serialize(sample, SampleJson.Default.StartupSample));
    }
}

internal sealed record StartupSample(FeatureGraph Graph, string Mode, long Bytes, string[] Features);

[JsonSerializable(typeof(StartupSample))]
internal partial class SampleJson : JsonSerializerContext;
