using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;

namespace Flamme.Tenants.Features;

/// <summary>
/// Enables tenant management endpoints.
/// </summary>
[DependsOn(typeof(TenantManagementFeature))]
public class TenantManagementEndpointsFeature(IModule serviceConfiguration) : FeatureBase(serviceConfiguration)
{
    /// <inheritdoc />
    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<TenantManagementEndpointsFeature>();
    }
}