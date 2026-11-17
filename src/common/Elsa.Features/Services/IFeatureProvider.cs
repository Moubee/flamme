using Elsa.Features.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Features.Services;

/// <summary>
/// Supplies feature construction and metadata to the shared module lifecycle.
/// </summary>
internal interface IFeatureProvider
{
    IFeature CreateFeature(Type featureType, IModule module);
    IReadOnlyList<Type> GetDependencies(Type featureType);
    IReadOnlyList<Type> GetConditionalDependencies(Type featureType);
    FeatureDescriptor GetDescriptor(Type featureType);
    ServiceDescriptor CreateHostedServiceDescriptor(Type hostedServiceType);
}