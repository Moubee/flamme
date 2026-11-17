using Elsa.Common.Serialization;
using Elsa.Features.Implementations;
using Elsa.NativeAot;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Features;
using Microsoft.Extensions.DependencyInjection;

return await ProbeRunner.RunAsync(args, "integrated", async () =>
{
    var services = new ServiceCollection();
    var module = new Module(services);
    Console.WriteLine("CHECK integrated.default-workflows-activation");
    module.Configure<WorkflowsFeature>();
    module.Apply();

    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    Console.WriteLine("CHECK integrated.resolve-runner");
    var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
    var awaited = false;
    var workflow = new Workflow(new Sequence
    {
        Activities =
        {
            new Inline(async _ =>
            {
                await Task.Yield();
                awaited = true;
            })
        }
    });
    Console.WriteLine("CHECK integrated.run-workflow");
    var result = await runner.RunAsync(workflow);
    ProbeRunner.Require(awaited && result.WorkflowState.SubStatus == WorkflowSubStatus.Finished,
        "The integrated production workflow must await its child and finish.");

    Console.WriteLine("CHECK integrated.production-payload");
    var registry = scope.ServiceProvider.GetRequiredService<ISerializationTypeRegistry>();
    registry.RegisterType(typeof(IntegratedPayload), "IntegratedPayload");
    var serializer = scope.ServiceProvider.GetRequiredService<IPayloadSerializer>();
    var json = serializer.Serialize(new IntegratedPayload { Value = "completed" });
    ProbeRunner.Require(serializer.Deserialize(json) is IntegratedPayload { Value: "completed" },
        "The integrated production serializer must round-trip its registered payload.");
});

public sealed class IntegratedPayload
{
    public string Value { get; set; } = "";
}
