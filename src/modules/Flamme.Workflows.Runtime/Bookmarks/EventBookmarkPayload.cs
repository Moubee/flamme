using Flamme.Common;
using Flamme.Workflows.Runtime.Stimuli;

namespace Flamme.Workflows.Runtime.Bookmarks;

[Obsolete("Use EventStimulus instead.")]
[ForwardedType(typeof(EventStimulus))]
public record EventBookmarkPayload(string EventName);