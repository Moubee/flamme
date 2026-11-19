using Flamme.Alterations.Core.Contexts;
using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Models;
using Flamme.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Pipelines.WorkflowExecution;

namespace Flamme.Alterations.Middleware.Workflows;

/// <summary>
/// Middleware that runs alterations.
/// </summary>
internal class RunAlterationsMiddleware(WorkflowMiddlewareDelegate next, IEnumerable<IAlterationHandler> handlers) : WorkflowExecutionMiddleware(next)
{
    public static readonly object AlterationsPropertyKey = new();
    public static readonly object AlterationsLogPropertyKey = new();

    public override async ValueTask InvokeAsync(WorkflowExecutionContext context)
    {
        var alterations = (IEnumerable<IAlteration>)(context.TransientProperties.GetValue(AlterationsPropertyKey) ?? throw new InvalidOperationException("No alterations found in the transient properties."));
        var log = (AlterationLog)(context.TransientProperties.GetValue(AlterationsLogPropertyKey) ?? throw new InvalidOperationException("No alteration log found in the transient properties."));
        await RunAsync(context, alterations, log, context.CancellationToken);
    }

    private async Task RunAsync(WorkflowExecutionContext workflowExecutionContext, IEnumerable<IAlteration> alterations, AlterationLog log, CancellationToken cancellationToken = default)
    {
        var commitActions = new List<Func<Task>>();

        foreach (var alteration in alterations)
        {
            // Find handlers.
            var supportedHandlers = handlers.Where(x => x.CanHandle(alteration)).ToList();

            foreach (var handler in supportedHandlers)
            {
                // Execute handler.
                var alterationContext = new AlterationContext(alteration, workflowExecutionContext, log, cancellationToken);
                await handler.HandleAsync(alterationContext);

                // If the handler has failed, exit.
                if (alterationContext.HasFailed)
                    return;

                // Collect the commit handler, if any.
                if (alterationContext.CommitAction != null)
                    commitActions.Add(alterationContext.CommitAction);
            }
        }

        // Execute commit handlers.
        foreach (var commitAction in commitActions)
            await commitAction();

        // Add alteration logs to the workflow execution log.
        foreach (var alterationLogEntry in log.LogEntries)
            workflowExecutionContext.AddExecutionLogEntry(alterationLogEntry.EventName ?? alterationLogEntry.Message, alterationLogEntry.Message);
    }
}