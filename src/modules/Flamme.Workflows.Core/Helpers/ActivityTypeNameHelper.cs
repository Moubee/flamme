using System.Reflection;
using Flamme.Workflows.Attributes;

namespace Flamme.Workflows.Helpers;

public static class ActivityTypeNameHelper
{
    public static int GetVersion(Type type) => GetConcreteBinding(type) is { } binding
        ? binding.Version
        : type.GetCustomAttribute<ActivityAttribute>()?.Version ?? 1;

    public static string? GenerateNamespace(Type activityType)
    {
        if (GetConcreteBinding(activityType) is { } binding)
            return binding.Namespace;

        var activityAttr = activityType.GetCustomAttribute<ActivityAttribute>();
        return activityAttr?.Namespace ?? activityType.Namespace;
    }

    public static string GenerateTypeName(Type type, string? ns)
    {
        if (GetConcreteBinding(type) is { } binding)
            return ns == null ? binding.Name : $"{ns}.{binding.Name}";

        var activityAttr = type.GetCustomAttribute<ActivityAttribute>();
        string typeName;
        
        if (type.IsGenericType)
        {
            var sanitizedTypeName = activityAttr?.Type ?? type.Name.Substring(0, type.Name.IndexOf("`", StringComparison.InvariantCulture));
            var genericArgs = type.GenericTypeArguments.Select(type1 => GenerateTypeName(type1, null));
            typeName = $"{sanitizedTypeName}<{string.Join(',', genericArgs)}>";
        }
        else
            typeName = activityAttr?.Type ?? type.Name;

        return ns != null ? $"{ns}.{typeName}" : typeName;
    }

    public static string GenerateTypeName<T>() where T : IActivity => GenerateTypeName(typeof(T));

    public static string GenerateTypeName(Type type)
    {
        if (GetConcreteBinding(type) is { } binding)
            return binding.TypeName;

        var ns = GenerateNamespace(type);
        return GenerateTypeName(type, ns);
    }

    private static GeneratedActivityBinding? GetConcreteBinding(Type type) =>
        !type.IsAbstract && !type.ContainsGenericParameters && typeof(IActivity).IsAssignableFrom(type)
            ? ActivityContextRegistry.GetRequiredBinding(type)
            : null;

    public static string? GetCategoryFromNamespace(string? ns)
    {
        if (string.IsNullOrWhiteSpace(ns))
            return null;

        var index = ns.LastIndexOf('.');

        return index < 0 ? ns : ns[(index + 1)..];
    }
}