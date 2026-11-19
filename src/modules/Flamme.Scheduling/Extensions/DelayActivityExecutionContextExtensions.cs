using Flamme.Common;
using Flamme.Scheduling;
using Flamme.Scheduling.Bookmarks;
using Flamme.Workflows;
using Flamme.Workflows.Models;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class DelayActivityExecutionContextExtensions
{
    /// <summary>
    /// Resumes the activity after a specified delay.
    /// </summary>
    /// <param name="context">The activity execution context in which the workflow is running.</param>
    /// <param name="delay">The delay before the workflow resumes.</param>
    /// <param name="callback">The delegate to execute when the activity resumes.</param>
    public static void DelayFor(this ActivityExecutionContext context, TimeSpan delay, ExecuteActivityDelegate? callback = null)
    {
        var timeProvider = context.ExpressionExecutionContext.GetRequiredService<TimeProvider>();
        var resumeAt = timeProvider.GetUtcNow().Add(delay);
        
        DelayUntil(context, resumeAt, callback);
    }

    /// <summary>
    /// Resumes the activity at a specified point in time.
    /// </summary>
    /// <param name="context">The activity execution context in which the workflow is running.</param>
    /// <param name="resumeAt">The point in time at which the workflow should resume execution.</param>
    /// <param name="callback">The delegate to execute when the activity resumes.</param>
    public static void DelayUntil(this ActivityExecutionContext context, DateTimeOffset resumeAt, ExecuteActivityDelegate? callback = null)
    {
        var payload = new DelayPayload(resumeAt);

        var bookmarkOptions = new CreateBookmarkArgs
        {
            BookmarkName = SchedulingStimulusNames.Delay,
            Stimulus = payload,
            Callback = callback
        };
        
        context.CreateBookmark(bookmarkOptions);
    }
}