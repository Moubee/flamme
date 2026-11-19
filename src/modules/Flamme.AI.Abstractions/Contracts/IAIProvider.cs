using Flamme.AI.Abstractions.Models;

namespace Flamme.AI.Abstractions.Contracts;

public interface IAIProvider
{
    string Name { get; }
    ValueTask<AISessionHandle> CreateSessionAsync(CreateAISessionRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AIProviderEvent> ExecuteTurnAsync(AITurnRequest request, IAIProviderToolInvoker toolInvoker, CancellationToken cancellationToken = default);
}

public interface IAIOrchestrator
{
    IAsyncEnumerable<AIStreamEvent> ExecuteChatAsync(AIChatRequest request, CancellationToken cancellationToken = default);
}

public interface IAIProviderToolInvoker
{
    ValueTask<AIToolResult> InvokeAsync(AIProviderToolInvocation invocation, CancellationToken cancellationToken = default);
}
