using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;

var run = BenchmarkSwitcher.FromAssembly(typeof(PublishedGraph).Assembly).Run(args);
return run.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;

// Each invocation launches the unmodified published workload as a fresh process.
// The BDN time includes process launch and exit; the child emits its own stage timings.
[SimpleJob(RunStrategy.ColdStart, launchCount: 1, warmupCount: 0, iterationCount: 10, invocationCount: 1)]
public class PublishedGraph
{
    [Params("managed", "native")]
    public string Runtime { get; set; } = "";

    private string _output = "";

    [Benchmark]
    public void FirstApplyAndExit() => Run("cold");

    [Benchmark]
    public void LookupsAndExit() => Run("compose");

    [Benchmark]
    public void WorkflowsAndExit() => Run("workflow");

    private void Run(string scenario)
    {
        var directory = Environment.GetEnvironmentVariable("ELSA_PUBLISHED_PROBES")
            ?? throw new InvalidOperationException("Set ELSA_PUBLISHED_PROBES to the absolute publication directory.");
        var info = new ProcessStartInfo(Path.Combine(directory, Runtime, "Workload"))
        {
            ArgumentList = { Runtime, scenario }, RedirectStandardOutput = true, UseShellExecute = false
        };
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Could not launch Workload.");
        _output = child.StandardOutput.ReadToEnd();
        child.WaitForExit();
        var expected = scenario == "compose" ? "lookup" : scenario;
        if (child.ExitCode != 0 || !_output.StartsWith($"{expected},{Runtime},", StringComparison.Ordinal))
            throw new InvalidOperationException($"The workload failed ({child.ExitCode}). {_output}");
    }

    [IterationCleanup]
    public void SaveSample()
    {
        var path = Environment.GetEnvironmentVariable("ELSA_PUBLISHED_SAMPLES")
            ?? throw new InvalidOperationException("Set ELSA_PUBLISHED_SAMPLES to an unused absolute CSV path.");
        File.AppendAllText(path, _output);
    }
}
