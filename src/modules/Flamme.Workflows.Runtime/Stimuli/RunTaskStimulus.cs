using Flamme.Workflows.Attributes;
using Flamme.Workflows.Runtime.Activities;

namespace Flamme.Workflows.Runtime.Stimuli;

/// <summary>
/// Contains information created by <see cref="RunTask"/>.  
/// </summary>
public record RunTaskStimulus(string TaskId, [property: ExcludeFromHash]string TaskName);