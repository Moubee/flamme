using Flamme.Alterations.Core.Results;

namespace Flamme.Alterations.Endpoints.Workflows.Retry;

/// <summary>
/// The response from the <see cref="Retry"/> endpoint.
/// </summary>
public record Response(ICollection<RunAlterationsResult> Results);