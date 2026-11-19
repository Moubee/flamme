using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Models;
using Flamme.Workflows.Api.Security;
using Flamme.Workflows.Management;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Requests;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Dispatch;

[UsedImplicitly]
internal class Endpoint(
    IWorkflowDefinitionService workflowDefinitionService,
    IWorkflowDispatcher workflowDispatcher,
    IIdentityGenerator identityGenerator,
    WorkflowDefinitionScriptAuthorizationService scriptAuthorizationService) : FlammeEndpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/workflow-definitions/{definitionId}/dispatch");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.Execute);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var definitionId = request.DefinitionId;
        var versionOptions = request.VersionOptions ?? VersionOptions.Published;
        var workflowGraph = await workflowDefinitionService.FindWorkflowGraphAsync(definitionId, versionOptions, cancellationToken);

        if(workflowGraph == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        var scriptAuthorizationResult = await scriptAuthorizationService.AuthorizeAsync(workflowGraph.Workflow, cancellationToken);
        if (!scriptAuthorizationResult.Succeeded)
        {
            await WorkflowDefinitionScriptAuthorizationFailure.SendAsync(scriptAuthorizationResult, message => AddError(message), Send.ErrorsAsync, cancellationToken);
            return;
        }
        
        var input = request.Input;
        
        if(input != null && input is not IDictionary<string, object>)
        {
            AddError("Input must be a dictionary.");
            await Send.ErrorsAsync(cancellation: cancellationToken);
            return;
        }

        var instanceId = request.InstanceId ?? identityGenerator.GenerateId();
        var dispatchRequest = new DispatchWorkflowDefinitionRequest(workflowGraph.Workflow.Identity.Id)
        {
            Input = input as IDictionary<string, object>,
            InstanceId = instanceId,
            CorrelationId = request.CorrelationId,
            TriggerActivityId = request.TriggerActivityId,
        };

        var options = new DispatchWorkflowOptions
        {
            Channel = request.Channel
        };

        var result = await workflowDispatcher.DispatchAsync(dispatchRequest, options, cancellationToken);
        
        if(!result.Succeeded)
        {
            var fault = result.Fault!;
            AddError(fault.Message, fault.Code);
            await Send.ErrorsAsync(cancellation: cancellationToken);
            return;
        }
        
        await Send.OkAsync(new Response(instanceId), cancellationToken);
    }

}
