using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Models;
using Flamme.Workflows.Api.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.GetManyById;

[PublicAPI]
internal class GetManyById(IWorkflowDefinitionStore store, IWorkflowDefinitionLinker linker) : FlammeEndpoint<Request>
{
    public override void Configure()
    {
        Get("/workflow-definitions/many-by-id");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.View);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            Ids = request.Ids
        };

        var definitions = (await store.FindManyAsync(filter, cancellationToken)).ToList();
        var models = await linker.MapAsync(definitions, cancellationToken);
        var response = new ListResponse<LinkedWorkflowDefinitionModel>(models);
        await Send.OkAsync(response, cancellationToken);
    }
}