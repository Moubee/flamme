using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Models;
using Flamme.Alterations.Workflows;
using Flamme.Common;
using Flamme.Common.Models;
using Flamme.Workflows;
using Flamme.Workflows.Management;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Contracts;
using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Alterations.Services;

/// <summary>
/// Stores the new plan and schedules it for immediate execution.
/// </summary>
public class DefaultAlterationPlanScheduler : IAlterationPlanScheduler
{
    private readonly IWorkflowDefinitionService _workflowDefinitionService;
    private readonly IWorkflowDispatcher _workflowDispatcher;
    private readonly IIdentityGenerator _identityGenerator;
    private readonly IJsonSerializer _jsonSerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAlterationPlanScheduler"/> class.
    /// </summary>
    public DefaultAlterationPlanScheduler(IWorkflowDefinitionService workflowDefinitionService, IWorkflowDispatcher workflowDispatcher, IIdentityGenerator identityGenerator, IJsonSerializer jsonSerializer)
    {
        _workflowDefinitionService = workflowDefinitionService;
        _workflowDispatcher = workflowDispatcher;
        _identityGenerator = identityGenerator;
        _jsonSerializer = jsonSerializer;
    }

    /// <inheritdoc />
    public async Task<string> SubmitAsync(AlterationPlanParams planParams, CancellationToken cancellationToken = default)
    {
        if(string.IsNullOrWhiteSpace(planParams.Id))
            planParams.Id = _identityGenerator.GenerateId();
        
        var definitionId = ExecuteAlterationPlanWorkflow.WorkflowDefinitionId;
        var workflowGraph = await _workflowDefinitionService.FindWorkflowGraphAsync(definitionId, VersionOptions.Published, cancellationToken);
        
        if (workflowGraph == null)
            throw new($"Workflow definition with ID '{definitionId}' not found");
        
        var serializedPlan = _jsonSerializer.Serialize(planParams);
        var request = new DispatchWorkflowDefinitionRequest(workflowGraph.Workflow.Identity.Id)
        {
            Input = new Dictionary<string, object>
            {
                ["Plan"] = serializedPlan
            }
        };
        await _workflowDispatcher.DispatchAsync(request, cancellationToken);

        return planParams.Id;
    }
}