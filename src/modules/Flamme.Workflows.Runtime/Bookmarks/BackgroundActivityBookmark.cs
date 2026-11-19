using Flamme.Common;
using Flamme.Workflows.Runtime.Stimuli;

namespace Flamme.Workflows.Runtime.Bookmarks;

/// <summary>
/// A bookmark that is used to resume a workflow that is waiting for a background activity to complete.
/// </summary>
[Obsolete("Use BackgroundActivityStimulus instead.")]
[ForwardedType(typeof(BackgroundActivityStimulus))]
public class BackgroundActivityBookmark
{
    /// <summary>
    /// Set retroactively after the job has been scheduled. It is used to cancel the job when the bookmark is deleted.
    /// </summary>
    public string? JobId { get; set; }
}