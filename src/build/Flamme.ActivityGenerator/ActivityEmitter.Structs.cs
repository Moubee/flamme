using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.ActivityGenerator;

namespace Flamme.ActivityGenerator;

internal sealed partial class ActivityEmitter
{
    // The IActivity argument already holds the activity's box. Do not cast a struct
    // through its value type, which copies it and loses writes to the actual activity.
    private static string InstanceTarget(INamedTypeSymbol type, string expression) =>
        type.IsValueType
            ? $"global::System.Runtime.CompilerServices.Unsafe.Unbox<{TypeName(type)}>({expression})"
            : $"(({TypeName(type)}){expression})";

    private static bool UsesByReferenceReceiver(IMethodSymbol method) =>
        method.ContainingType.IsValueType && !method.IsStatic && method.MethodKind != MethodKind.Constructor;

    private static string AccessorReceiverArgument(IMethodSymbol method, string instance)
    {
        if (method.IsStatic)
            return method.ContainingType.IsValueType ? "default" : "null!";
        return UsesByReferenceReceiver(method) ? "ref " + instance : instance;
    }
}
