namespace Flamme.Resilience.Options;

public class ResilienceOptions
{
    public ICollection<Type> StrategyTypes { get; set; } = new List<Type>();
}