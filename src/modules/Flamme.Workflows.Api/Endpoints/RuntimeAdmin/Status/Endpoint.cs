using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using FastEndpoints;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Status;

/// <summary>
/// <c>GET /admin/workflow-runtime/status</c> — returns the composite quiescence state, per-ingress-source state,
/// and active-execution cycle count. Always readable (subject to authorisation) — even during drain.
/// </summary>
[PublicAPI]
internal sealed class StatusEndpoint(IWorkflowRuntimeAdminService admin) : FlammeEndpointWithoutRequest<StatusResponse>
{
    public override void Configure()
    {
        Get("/admin/workflow-runtime/status");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Runtime, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(StatusResponseFactory.Build(admin.GetStatus()), ct);
    }
}
