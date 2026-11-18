using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args is not [("native" or "managed"), ("cold" or "compose" or "workflow")])
    throw new ArgumentException("Expected native|managed and cold|compose|workflow.");
if ((args[0] == "native") == RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("Unexpected executable format.");

var services = Host.CreateApplicationBuilder().Services;
var bytes = GC.GetTotalAllocatedBytes(true);
var start = Stopwatch.GetTimestamp();
var module = services.CreateModule();
module.Use<WorkflowsFeature>();
module.Apply();
var elapsed = Stopwatch.GetElapsedTime(start);
bytes = GC.GetTotalAllocatedBytes(true) - bytes;
if (module is not FixedFeatureModule || !module.HasFeature<WorkflowsFeature>())
    throw new InvalidOperationException("Expected a fixed graph containing WorkflowsFeature.");

if (args[1] == "cold")
{
    Console.WriteLine($"cold,{args[0]},{RuntimeInformation.RuntimeIdentifier},{elapsed.TotalMilliseconds:F6},{bytes},{System.Runtime.GCSettings.IsServerGC}");
    return;
}

const int lookups = 10_000_000;
Lookup(module, 100_000);
start = Stopwatch.GetTimestamp();
Lookup(module, lookups);
var lookupNs = Stopwatch.GetElapsedTime(start).TotalNanoseconds / lookups;

services.AddScoped<ICommitStateHandler, NoopCommitStateHandler>();
services.AddScoped<IWorkflowExecutionPipeline>(sp => new WorkflowExecutionPipeline(sp,
    pipeline => pipeline.UseExceptionHandling().UseDefaultActivityScheduler()));
services.AddScoped<IActivityExecutionPipeline>(sp => new ActivityExecutionPipeline(sp,
    pipeline => pipeline.UseExceptionHandling().UseDefaultActivityInvoker()));
await using var provider = services.BuildServiceProvider();
var features = provider.GetRequiredService<IInstalledFeatureRegistry>().List().Select(f => f.FullName).Order();
var names = string.Join('|', features);
if (args[1] == "compose")
    Console.WriteLine($"lookup,{args[0]},{RuntimeInformation.RuntimeIdentifier},{lookupNs:F6},{names},{System.Runtime.GCSettings.IsServerGC}");
if (args[1] == "workflow")
{
    await using var scope = provider.CreateAsyncScope();
    var scoped = scope.ServiceProvider;
    var registry = scoped.GetRequiredService<IActivityRegistry>();
    await registry.RegisterAsync<Workflow>();
    await registry.RegisterAsync<Inline>();
    int executions = 0;
    var workflow = Workflow.FromActivity(Inline.From(() => { executions++; }));
    var graph = await BuildGraph(scoped, workflow);
    if (graph.Nodes.Count != 2)
        throw new InvalidOperationException($"Expected Workflow and Inline in the graph, found {graph.Nodes.Count} nodes.");
    var runner = scoped.GetRequiredService<IWorkflowRunner>();
    const int warmup = 1_024;
    const int count = 4_096;
    await Run(warmup);
    start = Stopwatch.GetTimestamp();
    await Run(count);
    var seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
    if (executions != warmup + count) throw new InvalidOperationException($"Observed {executions} executions.");
    Console.WriteLine($"workflow,{args[0]},{RuntimeInformation.RuntimeIdentifier},{count / seconds:F3},{names},{System.Runtime.GCSettings.IsServerGC}");

    async Task Run(int repeats)
    {
        for (var i = 0; i < repeats; i++)
        {
            var state = (await runner.RunAsync(graph)).WorkflowState;
            if (state.SubStatus != WorkflowSubStatus.Finished)
            {
                var incidents = string.Join(Environment.NewLine, state.Incidents.Select(i => $"{i.Message}: {i.Exception?.InnerException?.Message} {i.Exception?.StackTrace}"));
                throw new InvalidOperationException($"Workflow ended in {state.SubStatus} after {executions} activity executions. {incidents}");
            }
        }
    }
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void Lookup(IModule module, int count)
{
    var hits = 0;
    for (var i = 0; i < count; i++)
        if (module.HasFeature<WorkflowsFeature>()) hits++;
    if (hits != count) throw new InvalidOperationException("WorkflowsFeature was absent.");
}

// PropertyBasedActivityResolver traverses the workflow's root through its property metadata.
[DynamicDependency(nameof(Composite.Root), typeof(Composite))]
static Task<Elsa.Workflows.Models.WorkflowGraph> BuildGraph(IServiceProvider services, Workflow workflow) =>
    services.GetRequiredService<IWorkflowGraphBuilder>().BuildAsync(workflow);
