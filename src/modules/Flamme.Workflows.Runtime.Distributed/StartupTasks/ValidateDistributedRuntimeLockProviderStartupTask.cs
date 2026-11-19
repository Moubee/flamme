using Flamme.Common;
using Flamme.Common.RecurringTasks;
using JetBrains.Annotations;

namespace Flamme.Workflows.Runtime.Distributed.StartupTasks;

/// <summary>
/// Checks the distributed runtime lock provider during startup.
/// </summary>
[UsedImplicitly]
[Order(-1000)]
public class ValidateDistributedRuntimeLockProviderStartupTask(DistributedRuntimeLockProviderValidator validator) : IStartupTask
{
    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        validator.Validate();
        return Task.CompletedTask;
    }
}
