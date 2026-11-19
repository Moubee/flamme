using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.Api.Endpoints.Tasks.Complete;

/// <summary>
/// Resumes the <see cref="RunTask"/> activity matching the received Task ID.
/// </summary>
public class Complete : FlammeEndpoint<Request, Response>
{
    private readonly ITaskReporter _taskReporter;

    /// <inheritdoc />
    public Complete(ITaskReporter taskReporter)
    {
        _taskReporter = taskReporter;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Post("/tasks/{taskId}/complete");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Tasks, "complete");
    }

    /// <inheritdoc />
    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        await _taskReporter.ReportCompletionAsync(request.TaskId, request.Result, cancellationToken);
        if (!HttpContext.Response.HasStarted) await Send.OkAsync(cancellation: cancellationToken);
    }
}
