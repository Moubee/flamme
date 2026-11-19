namespace Flamme.Workflows.LogPersistence;

public interface ILogPersistenceStrategyService
{
    IEnumerable<ILogPersistenceStrategy> ListStrategies();
}