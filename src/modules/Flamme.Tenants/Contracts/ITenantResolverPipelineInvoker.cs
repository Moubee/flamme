using Flamme.Common.Multitenancy;

namespace Flamme.Tenants;

public interface ITenantResolverPipelineInvoker
{
    Task<Tenant?> InvokePipelineAsync(CancellationToken cancellationToken = default);
}