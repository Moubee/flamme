using Flamme.Extensions;
using Flamme.Resilience.Serialization;
using Microsoft.Extensions.Configuration;

namespace Flamme.Resilience.StrategySources;

public class ConfigurationResilienceStrategySource(IConfiguration configuration, ResilienceStrategySerializer serializer) : IResilienceStrategySource
{
    public Task<IEnumerable<IResilienceStrategy>> GetStrategiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetStrategies());
    }
    
    private IEnumerable<IResilienceStrategy> GetStrategies()
    {
        var json = configuration.GetSectionAsJson("Resilience:Strategies");
        return string.IsNullOrWhiteSpace(json) ? [] : serializer.DeserializeMany(json);
    }
}