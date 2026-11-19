using Elsa.Common;
using Elsa.Workflows.Management;
using Elsa.Workflows.Runtime.Middleware.Workflows;
using Elsa.Workflows.Runtime.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime;

public class WorkflowHeartbeatGeneratorFactory(IOptions<RuntimeOptions> options, TimeProvider timeProvider, ILogger<WorkflowHeartbeatMiddleware> logger, ILoggerFactory loggerFactory)
{
    public HeartbeatGenerator CreateHeartbeatGenerator(WorkflowExecutionContext context)
    {
        var inactivityThreshold = options.Value.InactivityThreshold;
        var heartbeatInterval = TimeSpan.FromTicks((long)(inactivityThreshold.Ticks * 0.6));
        logger.LogDebug("Workflow heartbeat interval: {Interval}", heartbeatInterval);
        return new(async () => await UpdateTimestampAsync(context), heartbeatInterval, loggerFactory);
    }
    
    private async Task UpdateTimestampAsync(WorkflowExecutionContext context)
    {
        var workflowInstanceStore = context.GetRequiredService<IWorkflowInstanceStore>();
        var workflowInstanceId = context.Id;
        await workflowInstanceStore.UpdateUpdatedTimestampAsync(workflowInstanceId, timeProvider.GetUtcNow(), context.CancellationToken);
    }
}