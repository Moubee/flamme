using Flamme.Workflows.Runtime.HealthChecks;
using Flamme.Workflows.Runtime.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Flamme.Extensions;

/// <summary>
/// Adds Flamme workflow runtime readiness checks to ASP.NET Core health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Tag applied to Flamme health checks.
    /// </summary>
    public const string FlammeTag = "elsa";

    /// <summary>
    /// Tag applied to Flamme readiness checks.
    /// </summary>
    public const string ReadinessTag = "elsa-readiness";

    private static readonly string[] ReadinessTags = [FlammeTag, ReadinessTag];

    /// <summary>
    /// Adds conservative Flamme-specific readiness probes for the workflow runtime and its core stores.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="includePersistence">Whether to probe workflow management/runtime stores.</param>
    /// <param name="includeDistributedLocks">Whether to probe the configured distributed lock provider.</param>
    /// <param name="configureOptions">Configures Flamme readiness probe behavior.</param>
    public static IHealthChecksBuilder AddFlammeReadinessChecks(
        this IHealthChecksBuilder builder,
        bool includePersistence = true,
        bool includeDistributedLocks = false,
        Action<FlammeReadinessHealthCheckOptions>? configureOptions = null)
    {
        var optionsBuilder = builder.Services.AddOptions<FlammeReadinessHealthCheckOptions>();

        if (configureOptions != null)
            optionsBuilder.Configure(configureOptions);

        optionsBuilder
            .Validate(x => x.DistributedLockAcquisitionTimeout > TimeSpan.Zero, "Distributed lock acquisition timeout must be greater than zero.")
            .ValidateOnStart();

        builder.AddCheck<FlammeRuntimeHealthCheck>("elsa-runtime", tags: ReadinessTags);

        if (includePersistence)
            builder.AddCheck<FlammeWorkflowPersistenceHealthCheck>("elsa-workflow-persistence", tags: ReadinessTags);

        if (includeDistributedLocks)
            builder.AddCheck<FlammeDistributedLockHealthCheck>("elsa-distributed-locks", tags: ReadinessTags);

        return builder;
    }
}
