namespace Flamme.Resilience;

public interface IResilienceStrategySource
{
    Task<IEnumerable<IResilienceStrategy>> GetStrategiesAsync(CancellationToken cancellationToken = default);
}