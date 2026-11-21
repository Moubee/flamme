using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <summary>
/// Resolves compiled activity bindings independently of any host's catalog selection.
/// </summary>
public static class ActivityContextRegistry
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<Assembly, ActivityContext> Contexts = new();
    private static readonly Dictionary<Type, GeneratedActivityBinding> Bindings = new();

    /// <summary>
    /// Registers a producer's assembly inventory and exact-type bindings.
    /// </summary>
    public static void Register(ActivityContext context) => Register(context, includeInventory: true);

    /// <summary>
    /// Registers exact-type bindings from an explicit context.
    /// </summary>
    public static void RegisterBindings(ActivityContext context) => Register(context, includeInventory: false);

    public static ActivityContext GetRequiredContext(Assembly assembly)
    {
        lock (Sync)
        {
            if (Contexts.TryGetValue(assembly, out var context))
                return context;
        }

        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        lock (Sync)
        {
            if (Contexts.TryGetValue(assembly, out var context))
                return context;
        }

        throw new InvalidOperationException($"Module '{assembly.GetName().Name}' has no generated activity binding inventory. Build the module with the Flamme activity generator.");
    }

    public static GeneratedActivityBinding GetRequiredBinding(Type activityType) =>
        TryGetBinding(activityType, out var binding)
            ? binding
            : throw MissingBinding(activityType, "activity descriptor and runtime operations");

    public static bool TryGetBinding(Type activityType, [NotNullWhen(true)] out GeneratedActivityBinding? binding)
    {
        lock (Sync)
        {
            if (Bindings.TryGetValue(activityType, out binding))
                return true;
        }

        RuntimeHelpers.RunModuleConstructor(activityType.Module.ModuleHandle);

        lock (Sync)
            return Bindings.TryGetValue(activityType, out binding);
    }

    internal static InvalidOperationException MissingBinding(Type activityType, string binding) =>
        new($"Module '{activityType.Assembly.GetName().Name}', activity '{activityType}': missing generated binding for {binding}. Build the module with the Flamme activity generator or declare the exact activity type in an activity context.");

    private static void Register(ActivityContext context, bool includeInventory)
    {
        if (includeInventory)
        {
            var activityTypes = context.Bindings.Select(x => x.ActivityType).ToHashSet();
            foreach (var type in context.ExportedActivityTypes)
            {
                if (type.Assembly != context.Assembly || !activityTypes.Contains(type))
                    throw MissingBinding(type, "assembly inventory entry");
            }
        }

        var rows = new Dictionary<Type, GeneratedActivityBinding>();
        foreach (var row in context.Bindings)
        {
            var type = row.ActivityType;
            if (type.IsAbstract || type.ContainsGenericParameters || !typeof(IActivity).IsAssignableFrom(type))
                throw new InvalidOperationException($"Module '{context.Assembly.GetName().Name}' has a generated binding for '{type}', which must be a concrete activity type.");

            if (string.IsNullOrEmpty(row.TypeName) || string.IsNullOrEmpty(row.Name))
                throw MissingBinding(type, "persisted type name");
            if (row.CreateDescriptor is null)
                throw MissingBinding(type, "descriptor factory");
            if (row.GetOutcomeName is null || row.GetActivityPorts is null || row.BindBookmarkCallback is null || row.BindCompletionCallback is null)
                throw MissingBinding(type, "child traversal and callback operations");

            ValidateMembers(type, row.Inputs);
            ValidateMembers(type, row.Outputs);
            row.ValidateRuntimeFactories();

            if (!rows.TryAdd(type, row))
                throw new InvalidOperationException($"Module '{context.Assembly.GetName().Name}' supplies multiple generated bindings for activity '{type}'.");
        }

        lock (Sync)
        {
            if (includeInventory && Contexts.TryGetValue(context.Assembly, out var existingContext) && existingContext != context)
                throw new InvalidOperationException($"Module '{context.Assembly.GetName().Name}' supplies multiple generated activity binding inventories.");

            foreach (var (type, row) in rows)
            {
                if (Bindings.TryGetValue(type, out var existingRow) && !CanShareBinding(existingRow, row))
                    throw new InvalidOperationException($"Module '{context.Assembly.GetName().Name}' supplies a conflicting generated binding for activity '{type}'.");
            }

            foreach (var (type, row) in rows)
            {
                if (!Bindings.TryGetValue(type, out var existingRow) || (existingRow.IsInferred && !row.IsInferred))
                    Bindings[type] = row;
            }

            if (includeInventory)
                Contexts.TryAdd(context.Assembly, context);
        }
    }

    private static bool CanShareBinding(GeneratedActivityBinding existing, GeneratedActivityBinding candidate) =>
        existing == candidate ||
        (existing.IsInferred || candidate.IsInferred) &&
        existing.ActivityType.IsConstructedGenericType &&
        existing.TypeName == candidate.TypeName &&
        existing.Version == candidate.Version &&
        existing.Namespace == candidate.Namespace &&
        existing.Name == candidate.Name;

    private static void ValidateMembers(Type type, IReadOnlyList<ActivityMemberBinding> members)
    {
        foreach (var member in members)
        {
            if (member.GetValue is null || member.SetValue is null)
                throw MissingBinding(type, $"accessors for '{member.ClrName}'");

            if (typeof(Input).IsAssignableFrom(member.PropertyType) && member.CreateInput is null)
                throw MissingBinding(type, $"wrapped input factory for '{member.ClrName}'");
        }
    }
}
