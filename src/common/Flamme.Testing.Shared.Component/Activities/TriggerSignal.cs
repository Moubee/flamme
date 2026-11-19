using Flamme.Testing.Shared.Services;
using Flamme.Workflows;
using Flamme.Workflows.Runtime;

namespace Flamme.Testing.Shared.Activities;

public class TriggerSignal(string signal) : CodeActivity
{
    public string Signal { get; set; } = signal;

    protected override void Execute(ActivityExecutionContext context)
    {
        var signalManager = context.GetRequiredService<SignalManager>();
        context.DeferTask(() => signalManager.Trigger(Signal));
    }
}