using System.Text;
using Flamme.ActivityGenerator.Diagnostics;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.CodeGeneration;

internal sealed class ActivityAccessorWriter(string row)
{
    private readonly List<string> declarations = [];

    public void WriteDeclarations(StringBuilder source)
    {
        foreach (var declaration in declarations)
            source.AppendLine(declaration);
    }

    public string Call(IMethodSymbol method, string instance, string[] arguments, bool isProducerBridge = false)
    {
        var owner = method.ContainingType;
        var definition = method.OriginalDefinition;
        var name = $"{row}Access{declarations.Count}";
        var methodKind = method.MethodKind == MethodKind.Constructor ? "Constructor" : method.IsStatic ? "StaticMethod" : "Method";
        var attribute = method.MethodKind == MethodKind.Constructor ? "" : $", Name = {Quote(method.MetadataName)}";
        var returnType = method.MethodKind == MethodKind.Constructor ? owner.OriginalDefinition : definition.ReturnType;
        var parameters = definition.Parameters.Select(parameter => $"{TypeName(parameter.Type)} {Identifier(parameter.Name)}");
        if (method.MethodKind != MethodKind.Constructor)
            parameters = parameters.Prepend((UsesByReferenceReceiver(method) ? "ref " : "") + TypeName(owner.OriginalDefinition) + " owner");
        var declaration = $"        [UnsafeAccessor(UnsafeAccessorKind.{methodKind}{attribute})]\n" +
            $"        internal static extern {TypeName(returnType)} {name}({string.Join(", ", parameters)});";
        if (method.MethodKind != MethodKind.Constructor)
            arguments = arguments.Prepend(AccessorReceiverArgument(method, instance)).ToArray();

        var owners = ContainingTypes(owner).ToArray();
        var typeParameters = owners.SelectMany(type => type.OriginalDefinition.TypeParameters).ToArray();
        if (typeParameters.Length == 0 || isProducerBridge)
        {
            declarations.Add(declaration);
            return $"{name}({string.Join(", ", arguments)})";
        }

        // UnsafeAccessor requires generic parameters in their definition slots, even for a
        // row representing a closed type. This nested class mirrors the declaring constraints.
        if (typeParameters.Select(parameter => parameter.Name).Distinct().Count() != typeParameters.Length)
            throw new BindingException(method.MetadataName, "Use distinct generic parameter names in the declaring type and its enclosing types.");
        var bridge = name + "Bridge";
        var bridgeParameters = string.Join(", ", typeParameters.Select(parameter => Identifier(parameter.Name)));
        var constraints = string.Join(" ", typeParameters.Select(Constraints));
        declarations.Add($"        private static class {bridge}<{bridgeParameters}> {constraints}\n        {{\n{declaration}\n        }}");
        var typeArguments = string.Join(", ", owners.SelectMany(type => type.TypeArguments).Select(TypeName));
        return $"{bridge}<{typeArguments}>.{name}({string.Join(", ", arguments)})";
    }

    internal static string Constraints(ITypeParameterSymbol parameter)
    {
        var constraints = new List<string>();
        if (parameter.HasUnmanagedTypeConstraint)
            constraints.Add("unmanaged");
        else if (parameter.HasValueTypeConstraint)
            constraints.Add("struct");
        else if (parameter.HasReferenceTypeConstraint)
            constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        else if (parameter.HasNotNullConstraint)
            constraints.Add("notnull");
        constraints.AddRange(parameter.ConstraintTypes.Select(TypeName));
        if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint)
            constraints.Add("new()");
        if (parameter.AllowsRefLikeType)
            constraints.Add("allows ref struct");
        return constraints.Count == 0 ? "" : $"where {Identifier(parameter.Name)} : {string.Join(", ", constraints)}";
    }

    // The IActivity argument already holds the activity's box. Do not cast a struct
    // through its value type, which copies it and loses writes to the actual activity.
    internal static string InstanceTarget(INamedTypeSymbol type, string expression) =>
        type.IsValueType
            ? $"global::System.Runtime.CompilerServices.Unsafe.Unbox<{TypeName(type)}>({expression})"
            : $"(({TypeName(type)}){expression})";

    internal static bool UsesByReferenceReceiver(IMethodSymbol method) =>
        method.ContainingType.IsValueType && !method.IsStatic && method.MethodKind != MethodKind.Constructor;

    private static string AccessorReceiverArgument(IMethodSymbol method, string instance)
    {
        if (method.IsStatic)
            return method.ContainingType.IsValueType ? "default" : "null!";
        return UsesByReferenceReceiver(method) ? "ref " + instance : instance;
    }
}
