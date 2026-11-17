using Elsa.Common.Features;
using Elsa.Expressions.Features;
using Elsa.Features.Implementations;
using Elsa.NativeAot;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.CommitStates;
using Elsa.Workflows.Features;
using Microsoft.Extensions.DependencyInjection;

return await ProbeRunner.RunAsync(args, "execution", async () =>
{
    var services = new ServiceCollection();
    var module = new Module(services);

    // Explicit factories isolate the runner from the default-constructor activation probe.
    // These are the stock feature implementations and their complete dependency roles.
    Console.WriteLine("CHECK execution.production-registration");
    module.Configure(m => new TimeProviderFeature(m));
    module.Configure(m => new ExpressionsFeature(m));
    module.Configure(m => new MediatorFeature(m));
    module.Configure(m => new DefaultFormattersFeature(m));
    module.Configure(m => new MultitenancyFeature(m));
    module.Configure(m => new CommitStrategiesFeature(m));
    module.Configure(m => new WorkflowsFeature(m));
    module.Apply();

    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    Console.WriteLine("CHECK execution.resolve-runner");
    var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
    var trace = new List<string>();
    var workflow = new Workflow(new Sequence
    {
        Activities =
        {
            new Inline(async _ =>
            {
                await Task.Yield();
                trace.Add("awaited");
            }),
            new Inline(() => trace.Add("completed"))
        }
    });

    Console.WriteLine("CHECK execution.run-workflow");
    var result = await runner.RunAsync(workflow);
    ProbeRunner.Require(result.WorkflowState.SubStatus == WorkflowSubStatus.Finished,
        $"Expected Finished; received {result.WorkflowState.SubStatus}.");
    ProbeRunner.Require(trace.SequenceEqual(["awaited", "completed"]),
        "The production runner must await and complete both children in order.");
});
