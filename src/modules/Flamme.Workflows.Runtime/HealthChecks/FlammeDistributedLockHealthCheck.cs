using Flamme.Common;
using Flamme.Workflows.Runtime.Options;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flamme.Workflows.Runtime.HealthChecks;

/// <summary>
/// Verifies that the configured distributed lock provider can acquire and release a probe lock.
/// </summary>
public class FlammeDistributedLockHealthCheck(
    IServiceProvider serviceProvider,
    IOptions<FlammeReadinessHealthCheckOptions> options,
    ILogger<FlammeDistributedLockHealthCheck> logger) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var distributedLockProvider = serviceProvider.GetService<IDistributedLockProvider>();
            if (distributedLockProvider == null)
            {
                return HealthCheckResult.Degraded("Flamme distributed lock provider is not registered.", data: CreateData());
            }

            var lockName = $"elsa-health-check-{Guid.NewGuid():N}";
            await using var handle = await distributedLockProvider.TryAcquireLockAsync(lockName, options.Value.DistributedLockAcquisitionTimeout, cancellationToken);
            if (handle == null)
            {
                return HealthCheckResult.Degraded("Flamme distributed lock provider was reachable, but the probe lock was not acquired.", data: CreateData());
            }

            return HealthCheckResult.Healthy("Flamme distributed lock provider is reachable.", CreateData());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (!e.IsFatal())
        {
            logger.LogWarning(e, "Flamme distributed lock provider is not reachable.");
            return HealthCheckResult.Unhealthy("Flamme distributed lock provider is not reachable.", data: CreateData());
        }

        static Dictionary<string, object> CreateData() => new()
        {
            ["category"] = "distributed-locks"
        };
    }
}
