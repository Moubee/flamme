using Flamme.Common.Multitenancy;
using Flamme.Mediator;
using Flamme.Mediator.Contracts;
using Flamme.Tenants.Mediator;
using Flamme.Workflows.Runtime.Commands;
using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.Runtime.Responses;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// A simple implementation that queues the specified request for delivering stimuli on a non-durable background worker.
/// </summary>
public class BackgroundStimulusDispatcher(ICommandSender commandSender, ITenantAccessor tenantAccessor) : IStimulusDispatcher
{
    /// <inheritdoc />
    public async Task<DispatchStimulusResponse> SendAsync(DispatchStimulusRequest request, CancellationToken cancellationToken = default)
    {
        var command = new DispatchStimulusCommand(request);
        
        // Background commands run independently of caller's lifecycle.
        await commandSender.SendAsync(command, CommandStrategy.Background, CreateHeaders(), CancellationToken.None);
        return DispatchStimulusResponse.Empty;
    }

    private IDictionary<object, object> CreateHeaders()
    {
        return TenantHeaders.CreateHeaders(tenantAccessor.Tenant?.Id);
    }
}
