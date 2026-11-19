using Flamme.UserTasks.Contracts;
using Flamme.UserTasks.Models;
using Flamme.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.UserTasks.Services;

public sealed class DefaultUserTaskWorkflowResumer(IServiceProvider services) : IUserTaskWorkflowResumer
{
    public async Task ResumeAsync(UserTask task, UserTaskStimulus stimulus, CancellationToken cancellationToken = default)
    {
        var resumer = services.GetService<IWorkflowResumer>();
        if (resumer == null)
            throw new InvalidOperationException("A workflow resumer is required to complete a User Task.");

        await resumer.ResumeAsync(task.BookmarkId, new Dictionary<string, object>
        {
            [Flamme.UserTasks.Activities.UserTask.InputKey] = stimulus
        }, cancellationToken);
    }
}
