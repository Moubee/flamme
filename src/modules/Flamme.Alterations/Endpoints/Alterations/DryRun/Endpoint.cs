using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Models;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Alterations.Endpoints.Alterations.DryRun;

/// <summary>
/// Determines which workflow instances a "Submit" request would target without actually running an alteration.
/// </summary>
[PublicAPI]
public class DryRun(IWorkflowInstanceFinder workflowInstanceFinder) : FlammeEndpoint<AlterationWorkflowInstanceFilter, Response>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/alterations/dry-run");
        RequirePermission(Flamme.Alterations.Permissions.AlterationPermissions.Alterations, CoreVerbs.Execute);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(AlterationWorkflowInstanceFilter filter, CancellationToken cancellationToken)
    {
        if (!ValidateInput(filter))
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var workflowInstanceIds = await workflowInstanceFinder.FindAsync(filter, cancellationToken);
        var response = new Response(workflowInstanceIds.ToList());
        await Send.OkAsync(response, cancellationToken);
    }

    private bool ValidateInput(AlterationWorkflowInstanceFilter filter)
    {
        return TimestampFilterValidation.Validate(filter.TimestampFilters, error => AddError(error));
    }
}
