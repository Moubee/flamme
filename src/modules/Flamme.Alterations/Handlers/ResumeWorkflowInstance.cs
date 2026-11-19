using Flamme.Alterations.Core.Enums;
using Flamme.Alterations.Core.Notifications;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Contracts;
using Flamme.Workflows.Runtime.Requests;
using JetBrains.Annotations;

namespace Flamme.Alterations.Handlers;

/// <summary>
/// Resumes a workflow instance when an alteration job has been completed and the workflow contains scheduled work.
/// </summary>
[UsedImplicitly]
public class ResumeWorkflowInstance : INotificationHandler<AlterationJobCompleted>
{
    private readonly IWorkflowDispatcher _workflowDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeWorkflowInstance"/> class.
    /// </summary>
    public ResumeWorkflowInstance(IWorkflowDispatcher workflowDispatcher)
    {
        _workflowDispatcher = workflowDispatcher;
    }

    /// <inheritdoc />
    public async Task HandleAsync(AlterationJobCompleted notification, CancellationToken cancellationToken)
    {
        var job = notification.Job;
        
        // Check if job is completed.
        if(job.Status != AlterationJobStatus.Completed)
            return;
        
        // Check if the workflow contains scheduled work.
        if(!notification.WorkflowContainsScheduledWork)
            return;
        
        // Resume workflow instance.
        await _workflowDispatcher.DispatchAsync(new DispatchWorkflowInstanceRequest(job.WorkflowInstanceId), cancellationToken: cancellationToken);
    }
}