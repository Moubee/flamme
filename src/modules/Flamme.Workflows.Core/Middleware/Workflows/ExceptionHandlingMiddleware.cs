using Flamme.Workflows.Models;
using Flamme.Workflows.Pipelines.WorkflowExecution;
using Flamme.Workflows.State;
using Microsoft.Extensions.Logging;

namespace Flamme.Workflows.Middleware.Workflows;

/// <summary>
/// Adds extension methods to <see cref="ExceptionHandlingMiddleware"/>.
/// </summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// Installs the <see cref="ExceptionHandlingMiddleware"/> component in the activity execution pipeline.
    /// </summary>
    public static IWorkflowExecutionPipelineBuilder UseExceptionHandling(this IWorkflowExecutionPipelineBuilder pipelineBuilder) => pipelineBuilder.UseMiddleware<ExceptionHandlingMiddleware>();
}

/// <summary>
/// Catches any exceptions thrown by downstream components and transitions the workflow into the faulted state.
/// </summary>
public class ExceptionHandlingMiddleware : IWorkflowExecutionMiddleware
{
    private readonly WorkflowMiddlewareDelegate _next;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Constructor.
    /// </summary>
    public ExceptionHandlingMiddleware(WorkflowMiddlewareDelegate next, TimeProvider timeProvider, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask InvokeAsync(WorkflowExecutionContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
        {
            context.Cancel();
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "An exception was caught from a downstream middleware component");
            var exceptionState = ExceptionState.FromException(e);
            var now = _timeProvider.GetUtcNow();
            var activity = context.Workflow;
            var incident = new ActivityIncident(activity.Id, activity.NodeId ,activity.Type, e.Message, exceptionState, now);
            context.Exception ??= e;
            context.Incidents.Add(incident);
            context.TransitionTo(WorkflowSubStatus.Faulted);
            context.AddExecutionLogEntry("Faulted", e.Message, exceptionState);
        }
    }
}
