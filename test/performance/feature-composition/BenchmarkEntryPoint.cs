using System.Text.Json;
using BenchmarkDotNet.Running;

namespace Elsa.FeatureComposition.Benchmarks;

internal static class BenchmarkEntryPoint
{
    public static int Main(string[] args)
    {
        var samples = Directory.CreateDirectory(Path.Combine("BenchmarkDotNet.Artifacts", $"startup-{DateTime.UtcNow:yyyyMMddTHHmmss}-{Environment.ProcessId}"));
        Environment.SetEnvironmentVariable("ELSA_STARTUP_SAMPLES", samples.FullName);
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(BenchmarkEntryPoint).Assembly).Run(args).ToArray();
        if (summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success))) return 1;

        var records = samples.GetFiles("*.json").Select(f => JsonSerializer.Deserialize(File.ReadAllText(f.FullName), SampleJson.Default.StartupSample)!).ToArray();
        foreach (var group in records.GroupBy(r => r.Graph))
        {
            var expected = group.First().Features;
            if (group.Any(sample => !sample.Features.SequenceEqual(expected)))
                throw new InvalidOperationException($"{group.Key} installed different features across runs.");
            foreach (var mode in group.GroupBy(r => r.Mode))
                Console.WriteLine($"{group.Key} {mode.Key}: {expected.Length} features, first-invocation allocations (bytes): {string.Join(", ", mode.Select(s => s.Bytes).Order())}");
        }

        return 0;
    }
}
