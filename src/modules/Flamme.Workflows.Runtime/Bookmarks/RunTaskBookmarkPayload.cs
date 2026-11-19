using Flamme.Common;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Runtime.Activities;
using Flamme.Workflows.Runtime.Stimuli;

namespace Flamme.Workflows.Runtime.Bookmarks;

/// <summary>
/// Contains information created by <see cref="RunTask"/>.  
/// </summary>
[Obsolete("Use RunTaskStimulus instead.")]
[ForwardedType(typeof(RunTaskStimulus))]
public record RunTaskBookmarkPayload(string TaskId, [property: ExcludeFromHash] string TaskName);