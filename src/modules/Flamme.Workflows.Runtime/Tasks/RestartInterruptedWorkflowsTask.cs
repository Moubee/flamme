using Flamme.Common;
using Flamme.Common.Multitenancy;
using Flamme.Common.RecurringTasks;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Runtime.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flamme.Workflows.Runtime.Tasks;

[SingleNodeTask]
[UsedImplicitly]
public class RestartInterruptedWorkflowsTask(
    IWorkflowRestarter workflowRestarter, 
    IWorkflowInstanceStore workflowInstanceStore,
    ILogger<RestartInterruptedWorkflowsTask> logger,
    IOptions<RuntimeOptions> options, 
    TimeProvider timeProvider,
    ITenantService? tenantService = null,
    ITenantAccessor? tenantAccessor = null) : RecurringTask
{
    /// <inheritdoc />
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var workflowInstanceFilter = CreateWorkflowInstanceFilter();
        var batchSize = options.Value.RestartInterruptedWorkflowsBatchSize;
        var workflowInstances = workflowInstanceStore.EnumerateSummariesAsync(workflowInstanceFilter, batchSize, cancellationToken);

        logger.LogInformation("Restarting interrupted workflows.");
        await foreach (var workflowInstance in workflowInstances)
        {
            try
            {
                var tenantId = workflowInstance.TenantId ?? string.Empty;

                if (tenantService != null && tenantAccessor != null && !string.IsNullOrWhiteSpace(tenantId) && tenantId != Tenant.AgnosticTenantId)
                {
                    var tenant = await tenantService.FindAsync(tenantId, cancellationToken) ?? new Tenant { Id = tenantId, Name = tenantId };

                    using (tenantAccessor.PushContext(tenant))
                        await workflowRestarter.RestartWorkflowAsync(workflowInstance.Id, cancellationToken: cancellationToken);

                    continue;
                }

                await workflowRestarter.RestartWorkflowAsync(workflowInstance.Id, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to restart interrupted workflow {WorkflowInstanceId}", workflowInstance.Id);
            }
        }
        logger.LogInformation("Finished restarting interrupted workflows.");
    }

    private WorkflowInstanceFilter CreateWorkflowInstanceFilter()
    {
        var livenessThreshold = options.Value.InactivityThreshold;
        var now = timeProvider.GetUtcNow();
        var cutoffTimestamp = now - livenessThreshold;
        return new()
        {
            IsExecuting = true,
            BeforeLastUpdated = cutoffTimestamp,
            WorkflowStatus = WorkflowStatus.Running,
        };
    }
}