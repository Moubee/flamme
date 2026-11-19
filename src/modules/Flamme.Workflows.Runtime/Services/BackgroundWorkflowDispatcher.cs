using Flamme.Common.Multitenancy;
using Flamme.Mediator;
using Flamme.Mediator.Contracts;
using Flamme.Tenants.Mediator;
using Flamme.Workflows.Runtime.Commands;
using Flamme.Workflows.Runtime.Notifications;
using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.Runtime.Responses;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// A simple implementation that queues the specified request for workflow execution on a non-durable background worker.
/// </summary>
public class BackgroundWorkflowDispatcher(ICommandSender commandSender, INotificationSender notificationSender, ITenantAccessor tenantAccessor) : IWorkflowDispatcher
{
    /// <inheritdoc />
    public async Task<DispatchWorkflowResponse> DispatchAsync(DispatchWorkflowDefinitionRequest request, DispatchWorkflowOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Emit dispatching notification
        await notificationSender.SendAsync(new WorkflowDefinitionDispatching(request), cancellationToken);
        var command = WorkflowDispatchCommandFactory.CreateCommand(request);
        
        // Background commands run independently of caller's lifecycle.
        await commandSender.SendAsync(command, CommandStrategy.Background, CreateHeaders(), CancellationToken.None);
        var response = DispatchWorkflowResponse.Success();
        
        // Emit dispatched notification
        await notificationSender.SendAsync(new WorkflowDefinitionDispatched(request, response), cancellationToken);
        
        return response;
    }

    /// <inheritdoc />
    public async Task<DispatchWorkflowResponse> DispatchAsync(DispatchWorkflowInstanceRequest request, DispatchWorkflowOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Emit dispatching notification
        await notificationSender.SendAsync(new WorkflowInstanceDispatching(request), cancellationToken);
        var command = WorkflowDispatchCommandFactory.CreateCommand(request);

        // Background commands run independently of caller's lifecycle.
        await commandSender.SendAsync(command, CommandStrategy.Background, CreateHeaders(), CancellationToken.None);
        var response = DispatchWorkflowResponse.Success();
        
        // Emit dispatched notification
        await notificationSender.SendAsync(new WorkflowInstanceDispatched(request, response), cancellationToken);
        
        return response;
    }

    /// <inheritdoc />
    public async Task<DispatchWorkflowResponse> DispatchAsync(DispatchTriggerWorkflowsRequest request, DispatchWorkflowOptions? options = null, CancellationToken cancellationToken = default)
    {
        var command = WorkflowDispatchCommandFactory.CreateCommand(request);
        
        // Background commands run independently of caller's lifecycle.
        await commandSender.SendAsync(command, CommandStrategy.Background, CreateHeaders(), CancellationToken.None);
        return DispatchWorkflowResponse.Success();
    }

    /// <inheritdoc />
    public async Task<DispatchWorkflowResponse> DispatchAsync(DispatchResumeWorkflowsRequest request, DispatchWorkflowOptions? options = null, CancellationToken cancellationToken = default)
    {
        var command = WorkflowDispatchCommandFactory.CreateCommand(request);
        
        // Background commands run independently of caller's lifecycle.
        await commandSender.SendAsync(command, CommandStrategy.Background, CreateHeaders(), CancellationToken.None);
        return DispatchWorkflowResponse.Success();
    }
    
    private IDictionary<object, object> CreateHeaders()
    {
        return TenantHeaders.CreateHeaders(tenantAccessor.Tenant?.Id);
    }
}
