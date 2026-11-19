using Flamme.Expressions.Models;
using Flamme.Resilience.Models;

namespace Flamme.Resilience;

public interface IResilienceStrategyConfigEvaluator
{
    Task<IResilienceStrategy?> EvaluateAsync(ResilienceStrategyConfig? config, ExpressionExecutionContext context, CancellationToken cancellationToken = default);
}