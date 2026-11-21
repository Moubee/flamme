using System.Runtime.CompilerServices;

namespace Flamme.Workflows.Helpers;

internal static class ActivityCallbackNames
{
    private static readonly ConditionalWeakTable<Delegate, string> RestoredNames = new();

    public static TDelegate Remember<TDelegate>(TDelegate callback, string name) where TDelegate : Delegate
    {
        var existingName = RestoredNames.GetValue(callback, _ => name);

        if (!StringComparer.Ordinal.Equals(existingName, name))
            throw new InvalidOperationException($"The same activity callback has multiple persisted names ('{existingName}' and '{name}').");

        return callback;
    }

    public static string? GetName(Delegate? callback, Type owner)
    {
        if (callback == null)
            return null;

        if (RestoredNames.TryGetValue(callback, out var name))
            return name;

        name = callback.Method.Name;
        if (name.StartsWith('<'))
            throw UnsupportedCompilerCallback(owner, name);

        return name;
    }

    public static InvalidOperationException UnsupportedCompilerCallback(Type owner, string name) =>
        new($"Module '{owner.Assembly.GetName().Name}', activity '{owner}': compiler-generated callback '{name}' " +
            "cannot be persisted or restored. Use a named activity method and migrate existing state that stores this identifier.");
}
