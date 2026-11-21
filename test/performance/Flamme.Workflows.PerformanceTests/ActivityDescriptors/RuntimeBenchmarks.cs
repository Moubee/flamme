using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.CommitStates;
using Flamme.Workflows.Models;
using Flamme.Workflows.PortResolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.PerformanceTests.ActivityDescriptors;

[Config(typeof(Config)), JsonExporterAttribute.Full, GcServer(true), GcConcurrent(true)]
public class RuntimeBenchmarks
{
    private For _loop = null!;
    private If _branch = null!;
    private readonly PropertyBasedActivityResolver _resolver = new();
    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;
    private IWorkflowRunner _runner = null!;
    private string _completionName = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _loop = new For(0, 4, 1);
        _branch = new If(new Input<bool>(true))
        {
            Id = "branch",
            Then = new SetName(new Input<string>("then")) { Id = "then" },
            Else = new SetName(new Input<string>("else")) { Id = "else" }
        };
        _completionName = "OnChildCompleted";

        var constructed = ConstructIf();
        var inputs = ReadFourNamedInputs();
        var output = ReadOutput();
        var ports = (await _resolver.GetActivityPortsAsync(_branch)).ToDictionary(port => port.PortName);
        if (constructed.Type != "ControlFlow.If" || constructed.Version != 1)
            throw new InvalidOperationException("The If constructor must initialize its activity identity.");
        if (inputs.Count != 4 || inputs[nameof(For.Start)] != _loop.Start || inputs[nameof(For.End)] != _loop.End ||
            inputs[nameof(For.Step)] != _loop.Step || inputs[nameof(For.OuterBoundInclusive)] != _loop.OuterBoundInclusive ||
            output.Name != nameof(For.CurrentValue) || output.Value != _loop.CurrentValue)
            throw new InvalidOperationException("The loop bindings must return its four inputs and named output.");
        if (await ResolveTwoChildPorts() != 2 || ports.Count != 2 ||
            ports[nameof(_branch.Then)].Activity != _branch.Then || ports[nameof(_branch.Else)].Activity != _branch.Else)
            throw new InvalidOperationException("The If ports must reference both branches.");
        if (BindPrivateCompletionCallback() is null)
            throw new InvalidOperationException("The private completion callback must bind.");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationManager());
        services.AddFlamme();
        services.AddScoped<ICommitStateHandler, NoopCommitStateHandler>();
        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();
        var registry = _scope.ServiceProvider.GetRequiredService<IActivityRegistry>();
        await registry.RegisterAsync([typeof(Workflow), typeof(If), typeof(SetName)]);
        _runner = _scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var run = await RunIfWorkflow();
        if (run.WorkflowState.SubStatus != WorkflowSubStatus.Finished || run.WorkflowState.Name != "then")
            throw new InvalidOperationException($"Branch workflow produced {run.WorkflowState.SubStatus}, name '{run.WorkflowState.Name}'.");
    }

    [Benchmark]
    public If ConstructIf() => new(source: null, line: null);

    [Benchmark]
    public IDictionary<string, Input> ReadFourNamedInputs() => _loop.GetNamedInputs();

    [Benchmark]
    public NamedOutput ReadOutput() => _loop.GetOutputs().Single();

    [Benchmark]
    public async ValueTask<int> ResolveTwoChildPorts() => (await _resolver.GetActivityPortsAsync(_branch)).Count();

    [Benchmark]
    public ActivityCompletionCallback BindPrivateCompletionCallback() => _branch.GetActivityCompletionCallback(_completionName);

    [Benchmark]
    public Task<RunWorkflowResult> RunIfWorkflow() => _runner.RunAsync(_branch);

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _scope.Dispose();
        await _services.DisposeAsync();
    }
}
