using Microsoft.Extensions.Configuration;

namespace Flamme.Expressions.Liquid.Helpers;

public class ConfigurationSectionWrapper(IConfigurationSection section)
{
    public override string ToString() => section.Value!;

    public ConfigurationSectionWrapper GetSection(string name) => new(section.GetSection(name));
}