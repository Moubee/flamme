using Flamme.AI.Abstractions.Models;

namespace Flamme.AI.Abstractions.Contracts;

public interface IAIContextProvider
{
    string Kind { get; }
    ValueTask<AIResolvedContext> ResolveAsync(AIContextResolutionRequest request, CancellationToken cancellationToken = default);
}
