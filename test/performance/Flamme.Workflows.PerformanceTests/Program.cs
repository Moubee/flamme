using BenchmarkDotNet.Running;
using Flamme.Workflows.PerformanceTests;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Config).Assembly).Run(args).ToArray();
if (summaries.Length == 0)
{
    // Discovery and help commands intentionally produce no benchmark reports.
    return args.Any(arg => arg is "--list" or "--info" or "--version" or "--help") ? 0 : 1;
}

return summaries.Any(summary => summary.HasCriticalValidationErrors || summary.Reports.Length == 0 ||
    summary.Reports.Any(report => !report.Success)) ? 1 : 0;
