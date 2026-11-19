namespace Flamme.Workflows.Runtime.Options;

/// <summary>
/// Configures Flamme workflow runtime readiness health checks.
/// </summary>
public class FlammeReadinessHealthCheckOptions
{
    /// <summary>
    /// The maximum time the distributed-lock readiness probe waits to acquire its probe lock.
    /// </summary>
    public TimeSpan DistributedLockAcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether persistence readiness probing should continue after the first failed store probe.
    /// </summary>
    public bool ContinuePersistenceProbesAfterFailure { get; set; }
}
