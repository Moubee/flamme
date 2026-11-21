using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;

namespace Flamme.Workflows.PerformanceTests.ActivityDescriptors;

[Config(typeof(Config)), JsonExporterAttribute.Full, GcServer(true), GcConcurrent(true)]
public class StartupBenchmarks
{
    private ProcessStartInfo _start = null!;

    [GlobalSetup]
    public void Setup()
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "Flamme.ActivityStartupProbe.dll");
        if (!File.Exists(probe))
            throw new FileNotFoundException("Missing activity startup probe in the benchmark output.", probe);

        _start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            ArgumentList = { probe }
        };
        StartProcessAndConstructIf();
    }

    [Benchmark]
    public int StartProcessAndConstructIf()
    {
        using var child = Process.Start(_start)!;
        child.WaitForExit();
        if (child.ExitCode != 0)
            throw new InvalidOperationException($"Activity startup probe exited with code {child.ExitCode}.");
        return child.ExitCode;
    }
}
