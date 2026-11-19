using Flamme.Alterations.AlterationTypes;
using Flamme.Alterations.Core.Abstractions;
using Flamme.Alterations.Core.Contexts;
using Flamme.Common.Models;
using Flamme.Workflows.Management;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Alterations.AlterationHandlers;

/// <summary>
/// Upgrades the version of the workflow instance.
/// </summary>
[UsedImplicitly]
public class MigrateHandler : AlterationHandlerBase<Migrate>
{
    /// <inheritdoc />
    protected override async ValueTask HandleAsync(AlterationContext context, Migrate alteration)
    {
        var workflowDefinitionService = context.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
        var definitionId = context.Workflow.Identity.DefinitionId;
        var targetVersion = alteration.TargetVersion;
        var cancellationToken = context.CancellationToken;
        var targetWorkflowDefinition = await workflowDefinitionService.FindWorkflowDefinitionAsync(definitionId, VersionOptions.SpecificVersion(targetVersion), cancellationToken);
        
        if (targetWorkflowDefinition == null)
        {
            context.Fail($"Workflow definition with ID {definitionId} and version {targetVersion} not found");
            return;
        }
        
        var targetWorkflow = await workflowDefinitionService.MaterializeWorkflowAsync(targetWorkflowDefinition, cancellationToken);
        await context.WorkflowExecutionContext.SetWorkflowGraphAsync(targetWorkflow);
        
        context.Succeed();
    }
}