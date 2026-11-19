using Flamme.ExternalAuthentication.Contracts;

namespace Flamme.ExternalAuthentication.Stores.InMemory;

public sealed class InMemoryConnectionRegistryVersionStore : IConnectionRegistryVersionStore
{
    private long _version = 1;

    public ValueTask<long> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Interlocked.Read(ref _version));
    }

    public ValueTask<long> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Interlocked.Increment(ref _version));
    }

    public ValueTask<bool> IsCurrentAsync(long version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Interlocked.Read(ref _version) == version);
    }
}
