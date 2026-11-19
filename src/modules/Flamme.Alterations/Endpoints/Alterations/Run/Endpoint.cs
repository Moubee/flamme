using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Alterations.Core.Contracts;
using JetBrains.Annotations;

namespace Flamme.Alterations.Endpoints.Alterations.Run;

/// <summary>
/// Executes an alteration plan by targeting workflow instances by ID.
/// </summary>
[PublicAPI]
public class Run : FlammeEndpoint<Request, Response>
{
    private readonly IAlterationRunner _alterationRunner;
    private readonly IAlteredWorkflowDispatcher _workflowDispatcher;

    /// <inheritdoc />
    public Run(IAlterationRunner alterationRunner, IAlteredWorkflowDispatcher workflowDispatcher)
    {
        _alterationRunner = alterationRunner;
        _workflowDispatcher = workflowDispatcher;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Post("/alterations/run");
        RequirePermission(Flamme.Alterations.Permissions.AlterationPermissions.Alterations, CoreVerbs.Execute);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        // Run the alterations.
        var results = await _alterationRunner.RunAsync(request.WorkflowInstanceIds, request.Alterations, cancellationToken);

        // Schedule each successfully updated workflow containing scheduled work.
        await _workflowDispatcher.DispatchAsync(results, cancellationToken);

        // Write response.
        var response = new Response(results);
        await Send.OkAsync(response, cancellationToken);
    }
}