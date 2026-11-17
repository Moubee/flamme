using System.ComponentModel;
using System.Reflection;
using Elsa.Features.Attributes;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.Implementations;

/// <summary>
/// Preserves the existing reflection-based feature construction and metadata rules.
/// </summary>
internal sealed class ReflectionFeatureProvider : IFeatureProvider
{
    public IFeature CreateFeature(Type featureType, IModule module) => (IFeature)Activator.CreateInstance(featureType, module)!;

    public IReadOnlyList<Type> GetDependencies(Type featureType) => featureType
        .GetCustomAttributes<DependsOnAttribute>(true)
        .Select(attribute => attribute.Type)
        .ToArray();

    public IReadOnlyList<Type> GetConditionalDependencies(Type featureType) => featureType
        .GetCustomAttributes<DependencyOfAttribute>(true)
        .Select(attribute => attribute.Type)
        .ToArray();

    public FeatureDescriptor GetDescriptor(Type featureType)
    {
        var name = featureType.Name.Replace("Feature", string.Empty);
        var ns = "Elsa";
        var displayName = featureType.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? name;
        var description = featureType.GetCustomAttribute<DescriptionAttribute>()?.Description;
        return new(name, ns, displayName, description);
    }

    public ServiceDescriptor CreateHostedServiceDescriptor(Type hostedServiceType) => ServiceDescriptor.Singleton(typeof(IHostedService), hostedServiceType);
}