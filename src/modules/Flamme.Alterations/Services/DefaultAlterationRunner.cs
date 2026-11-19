using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Models;
using Flamme.Alterations.Core.Results;
using Flamme.Alterations.Middleware.Workflows;
using Flamme.Common;
using Flamme.Workflows;
using Flamme.Workflows.CommitStates;
using Flamme.Workflows.Management;
using Flamme.Workflows.Pipelines.WorkflowExecution;
using Flamme.Workflows.Runtime;
using Microsoft.Extensions.Logging;

namespace Flamme.Alterations.Services;

/// <inheritdoc />
public class DefaultAlterationRunner(
    IWorkflowRuntime workflowRuntime,
    IWorkflowExecutionPipeline workflowExecutionPipeline,
    IWorkflowDefinitionService workflowDefinitionService,
    IWorkflowStateExtractor workflowStateExtractor,
    ICommitStateHandler commitStateHandler,
    TimeProvider timeProvider,
    IServiceProvider serviceProvider)
    : IAlterationRunner
{
    /// <inheritdoc />
    public async Task<ICollection<RunAlterationsResult>> RunAsync(IEnumerable<string> workflowInstanceIds, IEnumerable<IAlteration> alterations, CancellationToken cancellationToken = default)
    {
        var results = new List<RunAlterationsResult>();
        var alterationList = alterations as ICollection<IAlteration> ?? alterations.ToList();

        foreach (var workflowInstanceId in workflowInstanceIds)
        {
            var result = await RunAsync(workflowInstanceId, alterationList, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<RunAlterationsResult> RunAsync(string workflowInstanceId, IEnumerable<IAlteration> alterations, CancellationToken cancellationToken = default)
    {
        var log = new AlterationLog(timeProvider);
        var result = new RunAlterationsResult(workflowInstanceId, log);
        var workflowClient = await workflowRuntime.CreateClientAsync(workflowInstanceId, cancellationToken: cancellationToken);

        // Load workflow instance.
        var workflowState = await workflowClient.ExportStateAsync(cancellationToken);

        if (workflowState == null)
        {
            log.Add($"Workflow instance with ID '{workflowInstanceId}' not found.", LogLevel.Error);
            return result;
        }

        // Load workflow definition.
        var workflowGraph = await workflowDefinitionService.FindWorkflowGraphAsync(workflowState.DefinitionVersionId, cancellationToken);

        if (workflowGraph == null)
        {
            log.Add($"Workflow definition with ID '{workflowState.DefinitionVersionId}' not found.", LogLevel.Error);
            return result;
        }

        // Create workflow execution context.
        var workflowExecutionContext = await WorkflowExecutionContext.CreateAsync(serviceProvider, workflowGraph, workflowState, cancellationToken: cancellationToken);
        workflowExecutionContext.TransientProperties.Add(RunAlterationsMiddleware.AlterationsPropertyKey, alterations);
        workflowExecutionContext.TransientProperties.Add(RunAlterationsMiddleware.AlterationsLogPropertyKey, log);

        // Build a new workflow execution pipeline.
        var pipelineBuilder = new WorkflowExecutionPipelineBuilder(serviceProvider);
        workflowExecutionPipeline.ConfigurePipelineBuilder(pipelineBuilder);

        // Replace the terminal DefaultActivitySchedulerMiddleware with the RunAlterationsMiddleware terminal.
        pipelineBuilder.ReplaceTerminal<RunAlterationsMiddleware>();

        // Build modified pipeline.
        var pipeline = pipelineBuilder.Build();

        // Execute the pipeline.
        await pipeline(workflowExecutionContext);

        // Extract workflow state.
        workflowState = workflowStateExtractor.Extract(workflowExecutionContext);
        
        // Commit workflow state.
        await commitStateHandler.CommitAsync(workflowExecutionContext, workflowState, cancellationToken);

        // Apply updated workflow state.
        // TODO: Importing back into the workflow runtime makes sense, but this also causes another SAVE ction of the workflow instance in the DB, which also happens in the previous step during the commit action.
        // Can we avoid this? Perhaps we need more granular control over when we purge and when we save to DB.
        await workflowClient.ImportStateAsync(workflowState, cancellationToken);

        // Check if the workflow has scheduled work.
        result.WorkflowHasScheduledWork = workflowExecutionContext.Scheduler.HasAny;

        return result;
    }
}