using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using FastEndpoints;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Pause;

/// <summary>
/// <c>POST /admin/workflow-runtime/pause</c> — places the runtime into <see cref="QuiescenceReason.AdministrativePause"/>.
/// Idempotent: subsequent calls return the current state without producing additional audit events (SC-007).
/// </summary>
[PublicAPI]
internal sealed class PauseEndpoint(IWorkflowRuntimeAdminService admin) : FlammeEndpoint<PauseRequest, StatusResponse>
{
    public override void Configure()
    {
        Post("/admin/workflow-runtime/pause");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Runtime, "control");
    }

    public override async Task HandleAsync(PauseRequest req, CancellationToken ct)
    {
        await admin.PauseAsync(req.Reason, HttpContext.User.Identity?.Name, ct);
        await Send.OkAsync(StatusResponseFactory.Build(admin.GetStatus()), ct);
    }
}
