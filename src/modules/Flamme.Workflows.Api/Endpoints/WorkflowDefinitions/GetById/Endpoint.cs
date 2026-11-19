using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.GetById;

[PublicAPI]
internal class GetById(IWorkflowDefinitionStore store, IWorkflowDefinitionLinker linker) : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Get("/workflow-definitions/by-id/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.View);
        Options(x => x.WithName("GetWorkflowDefinitionById"));
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id
        };

        var definition = await store.FindAsync(filter, cancellationToken);

        if (definition == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        var model = await linker.MapAsync(definition, cancellationToken);
        await Send.OkAsync(model, cancellationToken);
    }
}