using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Elsa.Extensions;
using Elsa.Features.Contracts;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.CommitStates;
using Elsa.Workflows.Features;
using Elsa.Workflows.Middleware.Activities;
using Elsa.Workflows.Middleware.Workflows;
using Elsa.Workflows.Pipelines.ActivityExecution;
using Elsa.Workflows.Pipelines.WorkflowExecution;

internal static class CompositionProbe
{
    public static async Task RunSmall()
    {
        var builder = WebApplication.CreateBuilder();
        var services = builder.Services;
        var start = Start();
        var module = services.CreateModule();
        module.Use<WorkflowsFeature>();
        module.Apply();
        var startup = Stop(start);
        await Measure(module, startup);
    }

    public static (long Bytes, long Timestamp) Start() =>
        (GC.GetTotalAllocatedBytes(precise: true), Stopwatch.GetTimestamp());

    public static (double Milliseconds, long Bytes) Stop((long Bytes, long Timestamp) start) =>
        (Stopwatch.GetElapsedTime(start.Timestamp).TotalMilliseconds, GC.GetTotalAllocatedBytes(precise: true) - start.Bytes);

    public static async Task Measure(IModule module, (double Milliseconds, long Bytes) startup)
    {
        const int lookups = 16_000_000;
        const int warmupWorkflows = 1_024;
        const int workflows = 4_096;
        Lookup(module, 100_000);
        var lookupStart = Stopwatch.GetTimestamp();
        Lookup(module, lookups);
        var lookupTime = Stopwatch.GetElapsedTime(lookupStart);

        // Keep the workflow in-process on both graphs. These overrides happen after startup timing.
        var services = module.Services;
        services.AddScoped<ICommitStateHandler, NoopCommitStateHandler>();
        services.AddScoped<IWorkflowExecutionPipeline>(sp => new WorkflowExecutionPipeline(sp,
            pipeline => pipeline.UseExceptionHandling().UseDefaultActivityScheduler()));
        services.AddScoped<IActivityExecutionPipeline>(sp => new ActivityExecutionPipeline(sp,
            pipeline => pipeline.UseExceptionHandling().UseDefaultActivityInvoker()));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        var features = scoped.GetRequiredService<IInstalledFeatureRegistry>().List().Select(f => f.FullName).Order().ToArray();
        var registry = scoped.GetRequiredService<IActivityRegistry>();
        await registry.RegisterAsync<Workflow>();
        await registry.RegisterAsync<Inline>();

        var executed = 0;
        var workflow = Workflow.FromActivity(Inline.From(() => { executed++; }));
        var graph = await scoped.GetRequiredService<IWorkflowGraphBuilder>().BuildAsync(workflow);
        var runner = scoped.GetRequiredService<IWorkflowRunner>();
        await Run(warmupWorkflows);
        var workflowStart = Stopwatch.GetTimestamp();
        await Run(workflows);
        var workflowTime = Stopwatch.GetElapsedTime(workflowStart);
        if (executed != warmupWorkflows + workflows)
            throw new InvalidOperationException($"Expected {warmupWorkflows + workflows} activity executions, observed {executed}.");

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            provider = module is FixedFeatureModule ? "Generated" : "Dynamic",
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            serverGc = System.Runtime.GCSettings.IsServerGC,
            features,
            startupMs = startup.Milliseconds,
            startupBytes = startup.Bytes,
            lookupNs = lookupTime.TotalNanoseconds / lookups,
            workflowsPerSecond = workflows / workflowTime.TotalSeconds
        }));

        async Task Run(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var result = await runner.RunAsync(graph);
                if (result.WorkflowState.SubStatus != WorkflowSubStatus.Finished)
                    throw new InvalidOperationException($"Workflow ended in {result.WorkflowState.SubStatus}.");
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Lookup(IModule module, int count)
    {
        var hits = 0;
        for (var i = 0; i < count; i++)
            if (module.HasFeature<WorkflowsFeature>()) hits++;
        if (hits != count) throw new InvalidOperationException("Missing WorkflowsFeature.");
    }
}
