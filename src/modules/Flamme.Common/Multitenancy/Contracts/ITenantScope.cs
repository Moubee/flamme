using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Common.Multitenancy;

public interface ITenantScope
{
    public IServiceScope ServiceScope { get; }
    IServiceProvider ServiceProvider { get; }
}