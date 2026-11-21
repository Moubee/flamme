using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Flamme.Extensions;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;
using Humanizer;

namespace Flamme.Workflows;

/// <inheritdoc />
public class ActivityDescriber(IPropertyDefaultValueResolver defaultValueResolver) : IActivityDescriber
{
    /// <inheritdoc />
    public Task<ActivityDescriptor> DescribeActivityAsync(Type activityType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var binding = ActivityContextRegistry.GetRequiredBinding(activityType);
        var descriptor = binding.CreateDescriptor(defaultValueResolver);
        descriptor.ClrType = activityType;
        descriptor.TypeName = binding.TypeName;
        descriptor.Name = binding.Name;
        descriptor.Namespace = binding.Namespace ?? string.Empty;
        descriptor.Version = binding.Version;
        descriptor.CustomProperties["Type"] = activityType;
        descriptor.Constructor = context =>
        {
            var result = context.CreateActivity(activityType);
            result.Activity.Type = binding.TypeName;
            return result;
        };

        return Task.FromResult(descriptor);
    }

    /// <inheritdoc />
    public IEnumerable<PropertyInfo> GetInputProperties([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType) =>
        activityType.GetProperties().Where(x => typeof(Input).IsAssignableFrom(x.PropertyType) || x.GetCustomAttribute<InputAttribute>() != null).DistinctBy(x => x.Name);

    /// <inheritdoc />
    public IEnumerable<PropertyInfo> GetOutputProperties([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type activityType) =>
        activityType.GetProperties().Where(x => typeof(Output).IsAssignableFrom(x.PropertyType)).DistinctBy(x => x.Name).ToList();

    /// <inheritdoc />
    public Task<OutputDescriptor> DescribeOutputPropertyAsync(PropertyInfo propertyInfo, CancellationToken cancellationToken = default)
    {
        var outputAttribute = propertyInfo.GetCustomAttribute<OutputAttribute>();
        var descriptionAttribute = propertyInfo.GetCustomAttribute<DescriptionAttribute>();
        var typeArgs = propertyInfo.PropertyType.GenericTypeArguments;
        var wrappedPropertyType = typeArgs.Any() ? typeArgs[0] : typeof(object);

        return Task.FromResult(new OutputDescriptor((outputAttribute?.Name ?? propertyInfo.Name).Pascalize(), outputAttribute?.DisplayName ?? propertyInfo.Name.Humanize(LetterCasing.Title), wrappedPropertyType, propertyInfo.GetValue, propertyInfo.SetValue, propertyInfo, descriptionAttribute?.Description ?? outputAttribute?.Description, outputAttribute?.IsBrowsable ?? true, outputAttribute?.IsSerializable));
    }

    /// <inheritdoc />
    public Task<InputDescriptor> DescribeInputPropertyAsync(PropertyInfo propertyInfo, CancellationToken cancellationToken = default)
    {
        var inputAttribute = propertyInfo.GetCustomAttribute<InputAttribute>();
        var descriptionAttribute = propertyInfo.GetCustomAttribute<DescriptionAttribute>();
        var propertyType = propertyInfo.PropertyType;
        var isWrappedProperty = typeof(Input).IsAssignableFrom(propertyType);
        var autoEvaluate = inputAttribute?.AutoEvaluate ?? true;
        var wrappedPropertyType = !isWrappedProperty ? propertyType : propertyInfo.PropertyType.GenericTypeArguments[0];

        if (wrappedPropertyType.IsNullableType())
            wrappedPropertyType = wrappedPropertyType.GetTypeOfNullable();

        var descriptor = new InputDescriptor(
            inputAttribute?.Name ?? propertyInfo.Name,
            wrappedPropertyType,
            propertyInfo.GetValue,
            propertyInfo.SetValue,
            isWrappedProperty,
            inputAttribute?.DisplayName ?? propertyInfo.Name.Humanize(LetterCasing.Title),
            descriptionAttribute?.Description ?? inputAttribute?.Description,
            inputAttribute?.Category,
            inputAttribute?.Order ?? 0,
            defaultValueResolver.GetDefaultValue(propertyInfo),
            inputAttribute?.DefaultSyntax,
            inputAttribute?.IsReadOnly ?? false,
            inputAttribute?.IsBrowsable ?? true,
            inputAttribute?.IsSerializable ?? true,
            false,
            autoEvaluate,
            inputAttribute?.EvaluatorType,
            null,
            propertyInfo
        )
        {
            IsSensitive = inputAttribute?.CanContainSecrets ?? false
        };

        return Task.FromResult(descriptor);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<InputDescriptor>> DescribeInputPropertiesAsync(Type activityType, CancellationToken cancellationToken = default) =>
        (await DescribeActivityAsync(activityType, cancellationToken)).Inputs;

    /// <inheritdoc />
    public async Task<IEnumerable<OutputDescriptor>> DescribeOutputPropertiesAsync(Type activityType, CancellationToken cancellationToken = default) =>
        (await DescribeActivityAsync(activityType, cancellationToken)).Outputs;
}
