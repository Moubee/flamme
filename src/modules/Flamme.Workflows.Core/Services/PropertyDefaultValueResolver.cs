using System.Reflection;
using Flamme.Workflows.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows;

/// <inheritdoc />
public class PropertyDefaultValueResolver(IServiceScopeFactory scopeFactory) : IPropertyDefaultValueResolver
{
    /// <inheritdoc />
    public object? GetDefaultValue(PropertyInfo activityPropertyInfo)
    {
        var inputAttribute = activityPropertyInfo.GetCustomAttribute<InputAttribute>();

        var declaringType = activityPropertyInfo.DeclaringType!;
        var context = new ActivityPropertyDefaultValueContext(
            activityPropertyInfo.ReflectedType ?? declaringType,
            activityPropertyInfo.Name,
            declaringType,
            activityPropertyInfo.PropertyType);

        return GetDefaultValue(context, inputAttribute?.DefaultValueProvider, inputAttribute?.DefaultValue);
    }

    /// <inheritdoc />
    public object? GetDefaultValue(ActivityPropertyDefaultValueContext context, Type? providerType, object? defaultValue)
    {
        if (providerType is null)
            return defaultValue;

        using var scope = scopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var provider = serviceProvider.GetService(providerType) ?? CreateProvider(serviceProvider, providerType);
        return ((IActivityPropertyDefaultValueProvider)provider).GetDefaultValue(context);
    }

    private static object CreateProvider(IServiceProvider services, Type providerType)
    {
        if (typeof(IActivity).IsAssignableFrom(providerType))
        {
            var binding = ActivityContextRegistry.GetRequiredBinding(providerType);
            var create = binding.CreateDefaultValueProvider
                ?? throw ActivityContextRegistry.MissingBinding(providerType, "default provider constructor");
            return create(services);
        }

        return ActivatorUtilities.CreateInstance(services, providerType);
    }
}
