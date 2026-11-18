using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Server.Web;
using Elsa.ServiceDefaults;
using Elsa.Workflows.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Perfolizer.Horology;

namespace Elsa.FeatureComposition.Benchmarks;

public enum FeatureGraph { Small, Web }

public abstract class CompositionBenchmark
{
    [Params(FeatureGraph.Small, FeatureGraph.Web)]
    public FeatureGraph Graph { get; set; }

    protected Func<IModule> Prepare()
    {
        Elsa.Expressions.Helpers.ObjectConverter.StrictMode = true;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(ReferenceServerComposition).Assembly.GetName().Name
        });
        if (Graph == FeatureGraph.Web)
        {
            builder.AddServiceDefaults();
            return new ReferenceServerComposition(builder).Apply;
        }

        var services = builder.Services;
        return () =>
        {
            var module = services.CreateModule();
            module.Use<WorkflowsFeature>();
            module.Apply();
            return module;
        };
    }

    protected static string CheckModule(IModule module)
    {
        var mode = module switch
        {
            FixedFeatureModule => "Generated",
            Features.Implementations.Module => "Dynamic",
            _ => throw new InvalidOperationException($"Unexpected module {module.GetType()}.")
        };
        if (mode != Environment.GetEnvironmentVariable("ELSA_BENCHMARK_MODE") || !module.HasFeature<WorkflowsFeature>())
            throw new InvalidOperationException($"Unexpected {mode} composition or missing WorkflowsFeature.");
        return mode;
    }
}

public class ColdCompositionConfig : CompositionConfig
{
    public ColdCompositionConfig() : base(Job.Default.WithStrategy(RunStrategy.ColdStart)
        .WithLaunchCount(10).WithWarmupCount(0).WithIterationCount(1).WithInvocationCount(1).WithUnrollFactor(1)) { }
}

public class WarmCompositionConfig : CompositionConfig
{
    public WarmCompositionConfig() : base(Job.ShortRun.WithLaunchCount(3).WithIterationCount(8).WithIterationTime(TimeInterval.FromMilliseconds(150))) { }
}

public abstract class CompositionConfig : ManualConfig
{
    private static readonly IToolchain Net11 = CreateToolchain();

    private static IToolchain CreateToolchain()
    {
        // 0.15.8's SDK validator throws for .NET 11. Use the stock project generator,
        // builder and executor with the target framework selected by the repo SDK.
        var sdk = CsProjCoreToolchain.From(new NetCoreAppSettings("net11.0", null, ".NET 11"));
        return new Toolchain(sdk.Name, sdk.Generator, sdk.Builder, sdk.Executor);
    }

    protected CompositionConfig(Job job)
    {
        Options |= ConfigOptions.DisableParallelBuild;
        BuildTimeout = TimeSpan.FromMinutes(10);
        foreach (var mode in new[] { "Dynamic", "Generated" })
        {
            var variant = job.WithToolchain(Net11).WithId(mode).WithGcServer(true)
                .WithEnvironmentVariable("ELSA_BENCHMARK_MODE", mode)
                .WithEnvironmentVariable("DOTNET_TieredCompilation", "0")
                .WithEnvironmentVariable("Logging__LogLevel__Default", "None")
                .WithArguments([new MsBuildArgument($"/p:ElsaExecutionMode={mode}"), new MsBuildArgument("/p:RunAnalyzers=true")]);
            AddJob(mode == "Dynamic" ? variant.AsBaseline() : variant);
        }
    }
}
