using Flamme.Api.Client.Shared.Models;

namespace Flamme.Api.Client.Resources.WorkflowDefinitions.Responses;

public record GetPathSegmentsResponse(ActivityNode ChildNode, ActivityNode Container, ICollection<ActivityPathSegment> PathSegments);