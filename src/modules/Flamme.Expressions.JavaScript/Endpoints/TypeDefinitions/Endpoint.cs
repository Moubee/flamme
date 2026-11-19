using Flamme.Authorization;
using System.Text;
using Flamme.Abstractions;
using Flamme.Common.Models;
using Flamme.Expressions.JavaScript.Permissions;
using Flamme.Expressions.JavaScript.TypeDefinitions.Contracts;
using Flamme.Expressions.JavaScript.TypeDefinitions.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Models;
using JetBrains.Annotations;

namespace Flamme.Expressions.JavaScript.Endpoints.TypeDefinitions;

/// <summary>
/// Returns a TypeScript definition that is used by the Monaco editor to display intellisense for JavaScript expressions.
/// </summary>
[PublicAPI]
internal class Get : FlammeEndpoint<Request>
{
    private readonly ITypeDefinitionService _typeDefinitionService;
    private readonly IWorkflowDefinitionService _workflowDefinitionService;

    /// <inheritdoc />
    public Get(ITypeDefinitionService typeDefinitionService, IWorkflowDefinitionService workflowDefinitionService)
    {
        _typeDefinitionService = typeDefinitionService;
        _workflowDefinitionService = workflowDefinitionService;
    }
    
    /// <inheritdoc />
    public override void Configure()
    {
        Post("scripting/javascript/type-definitions/{workflowDefinitionId}");
        RequirePermission(JavaScriptPermissions.Scripting, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var workflowGraph = await GetWorkflowGraphAsync(request.WorkflowDefinitionId, cancellationToken);

        if (workflowGraph == null)
        {
            AddError($"Workflow definition {request.WorkflowDefinitionId} not found");
            await Send.ErrorsAsync(cancellation: cancellationToken);
            return;
        }
        
        var typeDefinitionContext = new TypeDefinitionContext(workflowGraph, request.ActivityTypeName, request.PropertyName, cancellationToken);
        var typeDefinitions = await _typeDefinitionService.GenerateTypeDefinitionsAsync(typeDefinitionContext);
        var fileName = $"elsa.{request.WorkflowDefinitionId}.d.ts";
        var data = Encoding.UTF8.GetBytes(typeDefinitions);

        await Send.BytesAsync(data, fileName, "application/x-typescript", cancellation: cancellationToken);
    }

    private async Task<WorkflowGraph?> GetWorkflowGraphAsync(string workflowDefinitionId, CancellationToken cancellationToken)
    {
        return await _workflowDefinitionService.FindWorkflowGraphAsync(workflowDefinitionId, VersionOptions.Latest, cancellationToken);
    }
}

internal record Request(string WorkflowDefinitionId, string? ActivityTypeName, string? PropertyName)
{
    public Request() : this(null!, null!, null)
    {
    }
}