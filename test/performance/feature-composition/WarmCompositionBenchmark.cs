using BenchmarkDotNet.Attributes;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.CommitStates;
using Elsa.Workflows.Features;
using Elsa.Workflows.Middleware.Activities;
using Elsa.Workflows.Middleware.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Pipelines.ActivityExecution;
using Elsa.Workflows.Pipelines.WorkflowExecution;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.FeatureComposition.Benchmarks;

[Config(typeof(WarmCompositionConfig))]
[MemoryDiagnoser]
public class WarmCompositionBenchmark : CompositionBenchmark
{
    private IModule _module = null!;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private IWorkflowRunner _runner = null!;
    private WorkflowGraph _workflow = null!;
    private int _executions;

    [GlobalSetup]
    public async Task Setup()
    {
        var compose = Prepare();
        _module = compose();
        CheckModule(_module);
        var services = _module.Services;
        // Exercise the composed provider without starting hosted services or connecting to a database.
        services.AddScoped<ICommitStateHandler, NoopCommitStateHandler>();
        services.AddScoped<IWorkflowExecutionPipeline>(sp => new WorkflowExecutionPipeline(sp,
            pipeline => pipeline.UseExceptionHandling().UseDefaultActivityScheduler()));
        services.AddScoped<IActivityExecutionPipeline>(sp => new ActivityExecutionPipeline(sp,
            pipeline => pipeline.UseExceptionHandling().UseDefaultActivityInvoker()));

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateAsyncScope();
        var scoped = _scope.ServiceProvider;
        var registry = scoped.GetRequiredService<IActivityRegistry>();
        await registry.RegisterAsync<Workflow>();
        await registry.RegisterAsync<Inline>();
        var workflow = Workflow.FromActivity(Inline.From(() => { _executions++; }));
        _workflow = await scoped.GetRequiredService<IWorkflowGraphBuilder>().BuildAsync(workflow);
        _runner = scoped.GetRequiredService<IWorkflowRunner>();
        await RunWorkflow();
    }

    [Benchmark]
    public bool HasFeature() => _module.HasFeature<WorkflowsFeature>();

    [Benchmark]
    public async Task RunWorkflow()
    {
        var before = _executions;
        var result = await _runner.RunAsync(_workflow);
        if (result.WorkflowState.SubStatus != WorkflowSubStatus.Finished || _executions != before + 1)
            throw new InvalidOperationException("The workflow must finish and execute its activity exactly once.");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
