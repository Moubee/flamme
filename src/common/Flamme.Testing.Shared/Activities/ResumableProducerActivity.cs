using Flamme.Workflows;

namespace Flamme.Testing.Shared.Activities;

public sealed class ResumableProducerActivity<T> : CodeActivity
{
    public bool Resumed { get; private set; }
    private ValueTask Resume(T owner)
    {
        Resumed = true;
        return default;
    }
}
