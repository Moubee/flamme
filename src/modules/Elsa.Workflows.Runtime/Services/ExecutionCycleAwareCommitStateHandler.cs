using Elsa.Workflows.CommitStates;
using Elsa.Workflows.Runtime.Middleware.Workflows;
using Elsa.Workflows.State;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Decorator that disposes the execution-cycle handle stored in
/// <see cref="WorkflowExecutionContext.TransientProperties"/> AFTER the inner commit handler has persisted the
/// workflow's terminal state. This ordering is essential for the drain orchestrator's force-cancel path: the
/// orchestrator awaits <see cref="ExecutionCycleHandle.Disposed"/> with a settle timeout before it overwrites the
/// persisted sub-status with <see cref="WorkflowSubStatus.Interrupted"/>. Without this decorator the handle would
/// dispose at the end of the pipeline middleware (i.e., BEFORE commit), the orchestrator's wait would complete
/// prematurely, and the orchestrator's <c>Interrupted</c> write would either find no instance row (early dispatches)
/// or be clobbered by the runner's subsequent commit (mid-execution dispatches).
/// </summary>
public sealed class ExecutionCycleAwareCommitStateHandler : ICommitStateHandler
{
    private readonly ICommitStateHandler _inner;

    public ExecutionCycleAwareCommitStateHandler(DefaultCommitStateHandler inner) : this((ICommitStateHandler)inner) { }

    internal ExecutionCycleAwareCommitStateHandler(ICommitStateHandler inner) => _inner = inner;

    internal static void DecorateRegistrations(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(ICommitStateHandler) || descriptor.IsKeyedService || descriptor is DecoratedRegistration)
                continue;

            // Keep the original registration's lifetime and disposal ownership under a private key.
            var key = new object();
            var inner = descriptor switch
            {
                { ImplementationInstance: { } instance } => ServiceDescriptor.KeyedSingleton(descriptor.ServiceType, key, instance),
                { ImplementationType: { } type } => ServiceDescriptor.DescribeKeyed(descriptor.ServiceType, key, type, descriptor.Lifetime),
                _ => ServiceDescriptor.DescribeKeyed(descriptor.ServiceType, key, (sp, _) => descriptor.ImplementationFactory!(sp), descriptor.Lifetime)
            };
            services[i] = new DecoratedRegistration(key, descriptor.Lifetime);
            services.Add(inner);
        }
    }

    private sealed class DecoratedRegistration(object key, ServiceLifetime lifetime) : ServiceDescriptor(typeof(ICommitStateHandler), sp =>
    {
        var inner = sp.GetRequiredKeyedService<ICommitStateHandler>(key);
        return inner is ExecutionCycleAwareCommitStateHandler ? inner : new ExecutionCycleAwareCommitStateHandler(inner);
    }, lifetime);

    /// <inheritdoc />
    public Task CommitAsync(WorkflowExecutionContext workflowExecutionContext, CancellationToken cancellationToken = default) =>
        _inner.CommitAsync(workflowExecutionContext, cancellationToken);

    /// <inheritdoc />
    public Task CommitAsync(WorkflowExecutionContext workflowExecutionContext, WorkflowState workflowState, CancellationToken cancellationToken = default) =>
        _inner.CommitAsync(workflowExecutionContext, workflowState, cancellationToken);

    /// <inheritdoc />
    public async Task CommitFinalAsync(WorkflowExecutionContext workflowExecutionContext, WorkflowState workflowState, CancellationToken cancellationToken = default)
    {
        // A host commit handler can clear or replace transient properties while persisting.
        workflowExecutionContext.TransientProperties.TryGetValue(ExecutionCycleTrackingMiddleware.ExecutionCycleHandleKey, out var activeCycle);
        var handle = activeCycle as ExecutionCycleHandle;
        try { await _inner.CommitFinalAsync(workflowExecutionContext, workflowState, cancellationToken); }
        finally { handle?.Dispose(); }
    }
}
