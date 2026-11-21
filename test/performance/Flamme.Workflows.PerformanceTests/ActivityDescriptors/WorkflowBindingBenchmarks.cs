using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.CommitStates;
using Flamme.Workflows.Models;
using Flamme.Workflows.Options;
using Flamme.Workflows.Runtime.Activities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.PerformanceTests.ActivityDescriptors;

[Config(typeof(Config)), JsonExporterAttribute.Full, GcServer(true), GcConcurrent(true)]
public class WorkflowBindingBenchmarks
{
    private const string PublishedOutput = "PublishedValue";
    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;
    private IWorkflowRunner _runner = null!;
    private For _loop = null!;
    private ActivityExecutionContext _loopContext = null!;
    private WorkflowGraph _eventGraph = null!;
    private object _outputValue = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationManager());
        services.AddFlamme();
        services.AddScoped<ICommitStateHandler, NoopCommitStateHandler>();
        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();
        var provider = _scope.ServiceProvider;
        var registry = provider.GetRequiredService<IActivityRegistry>();
        await registry.RegisterAsync([typeof(Workflow), typeof(For), typeof(Sequence), typeof(Event), typeof(SetName)]);
        var builder = provider.GetRequiredService<IWorkflowGraphBuilder>();
        _runner = provider.GetRequiredService<IWorkflowRunner>();
        _loop = new For { Id = "loop", OuterBoundInclusive = null! };
        var loopGraph = await builder.BuildAsync(Workflow.FromActivity(_loop));
        var loopWorkflow = await WorkflowExecutionContext.CreateAsync(provider, loopGraph, "default-and-output");
        _loopContext = await loopWorkflow.CreateActivityExecutionContextAsync(_loop);
        _loopContext.ActivityDescriptor.Outputs.Single(output => output.Name == nameof(For.CurrentValue)).Name = PublishedOutput;
        _outputValue = "result";

        var initialBlocks = _loopContext.ExpressionExecutionContext.Memory.Blocks.Count;
        var first = await MaterializeOmittedInput();
        var firstWrapper = _loop.OuterBoundInclusive;
        var second = await MaterializeOmittedInput();
        if (!Equals(first, true) || !Equals(second, true) || firstWrapper is null || _loop.OuterBoundInclusive is null ||
            ReferenceEquals(firstWrapper, _loop.OuterBoundInclusive) ||
            _loopContext.ExpressionExecutionContext.Memory.Blocks.Count != initialBlocks + 1)
            throw new InvalidOperationException("The default must create one fresh Boolean input and retain one memory block.");

        var register = RecordRenamedOutput();
        if (!ReferenceEquals(register.FindOutputByActivityId(_loop.Id, PublishedOutput), _outputValue) ||
            register.FindMany(_loop.Id, nameof(For.CurrentValue)).Any())
            throw new InvalidOperationException("The output must be recorded under the published name.");

        var sequence = new Sequence
        {
            Id = "sequence",
            Activities =
            [
                new Event("benchmark-signal") { Id = "wait" },
                new SetName(new Input<string>("continued")) { Id = "after" }
            ]
        };
        _eventGraph = await builder.BuildAsync(Workflow.FromActivity(sequence));
        var suspended = await _runner.RunAsync(_eventGraph);
        var bookmark = suspended.WorkflowState.Bookmarks.Single();
        if (suspended.WorkflowState.SubStatus != WorkflowSubStatus.Suspended || bookmark.CallbackMethodName != "CompleteInternalAsync")
            throw new InvalidOperationException("The Event must suspend with its named resume callback.");
        var resumed = await ResumeEvent(suspended);
        if (resumed.WorkflowState.SubStatus != WorkflowSubStatus.Finished || resumed.WorkflowState.Name != "continued" ||
            resumed.WorkflowState.Bookmarks.Any() || resumed.WorkflowState.Incidents.Any())
            throw new InvalidOperationException("Restoring the bookmark must run the following activity and finish the workflow.");
    }

    [Benchmark]
    public Task<object?> MaterializeOmittedInput()
    {
        // Removing the previous generated literal keeps this reuse of a live execution context bounded.
        if (_loop.OuterBoundInclusive is { } previous)
            _loopContext.ExpressionExecutionContext.Memory.Blocks.Remove(previous.MemoryBlockReference().Id);
        _loop.OuterBoundInclusive = null!;
        return _loopContext.EvaluateInputPropertyAsync(nameof(For.OuterBoundInclusive));
    }

    [Benchmark]
    public ActivityOutputRegister RecordRenamedOutput()
    {
        // One new register per operation models the first published output and avoids retaining execution history.
        var register = new ActivityOutputRegister();
        register.Record(_loopContext, nameof(For.CurrentValue), _outputValue);
        return register;
    }

    [Benchmark]
    public async Task<RunWorkflowResult> SuspendAndResumeEvent()
    {
        var suspended = await _runner.RunAsync(_eventGraph);
        return await ResumeEvent(suspended);
    }

    private Task<RunWorkflowResult> ResumeEvent(RunWorkflowResult suspended)
    {
        var state = suspended.WorkflowState;
        return _runner.RunAsync(_eventGraph, state, new RunWorkflowOptions { BookmarkId = state.Bookmarks.Single().Id });
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        ((IDisposable)_loopContext).Dispose();
        _scope.Dispose();
        await _services.DisposeAsync();
    }
}
