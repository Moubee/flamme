using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Entities;
using Flamme.Common.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Version;

[PublicAPI]
internal class ListVersions(IWorkflowDefinitionStore store) : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Get("workflow-definitions/{definitionId}/versions");

        // Anyone who can read a definition can already read any of its versions, and the designer lists them to open one.
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Definitions, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var definitionId = Route<string>("definitionId")!;
        
        var filter = new WorkflowDefinitionFilter
        {
            DefinitionId = definitionId,
            VersionOptions = VersionOptions.All
        };
        
        var orderBy = new WorkflowDefinitionOrder<int>(x => x.Version, OrderDirection.Descending);
        var definitions = await store.FindManyAsync(filter, orderBy, cancellationToken);
        
        if (!definitions.Any())
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(definitions, cancellationToken);
    }
}