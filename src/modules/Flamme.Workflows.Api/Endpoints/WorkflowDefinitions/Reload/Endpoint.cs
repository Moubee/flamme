using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Reload;

[PublicAPI]
internal class Reload(IWorkflowDefinitionsReloader workflowDefinitionsReloader) : FlammeEndpointWithoutRequest
{
    private const int BatchSize = 10;

    public override void Configure()
    {
        Post("/actions/workflow-definitions/reload");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, "reload");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        await ReloadWorkflowDefinitionsAsync(cancellationToken);
        await Send.OkAsync(cancellation: cancellationToken);
    }

    private async Task ReloadWorkflowDefinitionsAsync(CancellationToken cancellationToken)
    {
        await workflowDefinitionsReloader.ReloadWorkflowDefinitionsAsync(cancellationToken);
    }
}