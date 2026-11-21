using System.Text;
using Flamme.ActivityGenerator.Diagnostics;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.CodeGeneration;

internal sealed partial class ActivityCallbackWriter(
    Compilation compilation, INamedTypeSymbol activity, string row, ActivityAccessorWriter accessors, bool isProducerBridge = false)
{
    private static readonly (string Kind, string Context, string Delegate)[] CallbackKinds =
    [
        ("Bookmark", "ActivityExecutionContext", "ExecuteActivityDelegate"),
        ("Completion", "ActivityCompletedContext", "ActivityCompletionCallback")
    ];

    public void WriteBindings(StringBuilder source)
    {
        foreach (var (kind, context, delegateType) in CallbackKinds)
            EmitCallbacks(source, kind, context, delegateType);
    }

    public void WriteProducerBridge(StringBuilder source, IEnumerable<IPropertySymbol> properties)
    {
        var declarations = CallbackLookupMethods(activity).ToArray();
        var callbacks = CallbackKinds.SelectMany(kind => declarations.Where(method =>
                SupportsCallback(method, kind.Context) && method.DeclaredAccessibility != Accessibility.Public)
            .Select(method => (kind.Kind, kind.Delegate, Method: method))).ToArray();
        var specialized = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var method in declarations.Where(CanSpecializeCallback))
            specialized.Add(method, $"SpecializedCallback_{specialized.Count}");
        var lookupMetadata = CallbackLookupMetadata(declarations, specialized);
        var memberAccessors = properties
            .SelectMany(property => new[] { (Method: property.GetMethod, Prefix: "Get_"), (Method: property.SetMethod, Prefix: "Set_") })
            .Where(accessor => accessor.Method is { DeclaredAccessibility: not Accessibility.Public } ||
                               accessor.Method is { IsInitOnly: true }).ToArray();
        if (callbacks.Length == 0 && memberAccessors.Length == 0 && lookupMetadata.Length == 0)
            return;

        var typeParameters = ContainingTypes(activity).SelectMany(owner => owner.TypeParameters).ToArray();
        if (typeParameters.Select(parameter => parameter.Name).Distinct(StringComparer.Ordinal).Count() != typeParameters.Length)
            throw new BindingException("producer bridge type parameters", "Use distinct generic parameter names in the declaring type and its enclosing types.", SourceLocations(activity));
        var typeArguments = typeParameters.Length == 0 ? "" : $"<{string.Join(", ", typeParameters.Select(p => Identifier(p.Name)))}>";
        source.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        source.AppendLine($"public static class {row}{typeArguments} {string.Join(" ", typeParameters.Select(ActivityAccessorWriter.Constraints))}");
        source.AppendLine("{");
        for (var index = 0; index < lookupMetadata.Length; index++)
            source.AppendLine($"    public const string {CallbackLookupPrefix}{index} = {Quote(lookupMetadata[index])};");
        foreach (var entry in specialized)
        {
            var method = entry.Key;
            var parameter = TypeName(method.Parameters[0].Type);
            var target = ActivityAccessorWriter.InstanceTarget(activity, "activity");
            var call = accessors.Call(method, target, ["context"], isProducerBridge);
            source.AppendLine($"    public static {TypeName(method.ReturnType)} {entry.Value}(IActivity activity, {parameter} context) => {call};");
        }
        // A name can accept more than one callback signature through contravariance.
        // Keep one exported entry per kind and name and use the selected CLR signature to invoke it.
        foreach (var group in callbacks.GroupBy(callback => (callback.Kind, callback.Delegate, callback.Method.Name)))
        {
            var (kind, delegateType, name) = group.Key;
            source.AppendLine($"    public static {delegateType} {kind}_{name}(IActivity activity, string signature) => signature switch");
            source.AppendLine("    {");
            foreach (var callback in group)
                source.AppendLine($"        {Quote(CallbackSignature(callback.Method))} => {BindCallback(callback.Method)},");
            source.AppendLine("        _ => throw new ArgumentException(\"Unknown callback overload signature.\", nameof(signature)),");
            source.AppendLine("    };");
        }
        foreach (var (method, prefix) in memberAccessors)
        {
            var property = (IPropertySymbol)method!.AssociatedSymbol!;
            if (property.Type.IsRefLikeType || property.RefKind != RefKind.None || property.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
                throw new BindingException("property " + property.Name, "Use an ordinary property type for a callable inherited accessor.",
                    SourceLocations(property));
            var name = prefix + property.Name;
            var target = ActivityAccessorWriter.InstanceTarget(activity, "activity");
            if (method.MethodKind == MethodKind.PropertyGet)
                source.AppendLine($"    public static object? {name}(IActivity activity) => {accessors.Call(method, target, [], isProducerBridge)};");
            else
            {
                var value = $"value is null ? default : ({TypeName(property.Type)})value";
                source.AppendLine($"    public static void {name}(IActivity activity, object? value) => {accessors.Call(method, target, [value], isProducerBridge)};");
            }
        }
        accessors.WriteDeclarations(source);
        source.AppendLine("}");
    }

    private void EmitCallbacks(StringBuilder source, string kind, string context, string delegateType)
    {
        source.AppendLine($"    private static {delegateType}? Bind{row}{kind}(IActivity activity, string name) => name switch");
        source.AppendLine("    {");
        foreach (var group in CallbackCandidates(kind, context).GroupBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            var candidates = group.ToArray();
            // Reflection looked within each type's name scope before descending to the
            // next base. Delegate compatibility was checked only after choosing a method.
            var scope = candidates.Any(candidate => candidate.Depth == 0 || !candidate.IsPrivate)
                ? 0 : candidates.Min(candidate => candidate.Depth);
            var visible = candidates.Where(candidate => candidate.Depth == scope || !candidate.IsPrivate).ToArray();
            var firstDepth = visible[0].Depth;
            var ambiguous = visible.Any(candidate => candidate.Signature != visible[0].Signature) ||
                            visible.Count(candidate => candidate.Depth == firstDepth) > 1;
            if (!ambiguous && !candidates.Any(candidate => candidate.SupportsCallback))
                continue;
            var selected = visible[0];
            var binding = ambiguous
                ? $"throw new AmbiguousMatchException({Quote($"Ambiguous callback '{selected.Name}' on activity '{TypeName(activity)}'.")})"
                : selected.SupportsCallback
                  ? selected.Bridge ?? BindCallback(selected.Method!)
                  : "throw new ArgumentException(\"Cannot bind to the target method because its signature is not compatible.\")";
            source.AppendLine($"        {Quote(group.Key)} => {binding},");
        }
        source.AppendLine("        _ => null,");
        source.AppendLine("    };");
    }

    internal static INamedTypeSymbol? FindProducerBridge(Compilation compilation, INamedTypeSymbol owner)
    {
        var arguments = ContainingTypes(owner).SelectMany(type => type.TypeArguments).ToArray();
        var suffix = arguments.Length == 0 ? "" : "`" + arguments.Length;
        var bridge = compilation.GetTypeByMetadataName(Workflows + "Generated." + ActivityGeneratedNames.BridgeName(owner) + suffix);
        return bridge is null || arguments.Length == 0 ? bridge : bridge.Construct(arguments);
    }

    private string BindCallback(IMethodSymbol method)
    {
        var ownerName = TypeName(method.ContainingType);
        var target = method.IsStatic ? ownerName : ActivityAccessorWriter.InstanceTarget(method.ContainingType, "activity");
        if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly) || method.MethodKind != MethodKind.Ordinary)
            return $"context => {accessors.Call(method, target, ["context"], isProducerBridge)}";

        var member = target + "." + Identifier(method.Name);
        return ActivityAccessorWriter.UsesByReferenceReceiver(method) ? $"context => {member}(context)" : member;
    }
}
