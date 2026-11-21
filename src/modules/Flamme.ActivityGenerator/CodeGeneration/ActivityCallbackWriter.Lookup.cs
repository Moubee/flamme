using Flamme.ActivityGenerator.Diagnostics;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.CodeGeneration;

internal sealed partial class ActivityCallbackWriter
{
    private const string CallbackLookupPrefix = "CallbackLookup_";

    private sealed record CallbackCandidate(string Name, string Signature, int Depth, bool IsPrivate, bool SupportsCallback,
        IMethodSymbol? Method = null, string? Bridge = null);

    private static IEnumerable<IMethodSymbol> CallbackLookupMethods(INamedTypeSymbol owner) =>
        owner.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Ordinary);

    private bool SupportsCallback(IMethodSymbol method, string context)
    {
        if (method.Arity != 0 || method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.Parameters.Length != 1 || method.Parameters[0].RefKind != RefKind.None ||
            !Is(method.ReturnType, "System.Threading.Tasks.ValueTask"))
            return false;

        var contextType = compilation.GetTypeByMetadataName(Workflows + context)!;
        var conversion = compilation.ClassifyCommonConversion(contextType, method.Parameters[0].Type);
        return conversion.IsIdentity || conversion is { IsImplicit: true, IsReference: true };
    }

    private string[] CallbackLookupMetadata(IMethodSymbol[] methods)
    {
        var callbackNames = new HashSet<string>(methods.GroupBy(method => method.Name, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any() || group.Any(method => CallbackKinds.Any(kind => SupportsCallback(method, kind.Context))))
            .Select(group => group.Key), StringComparer.Ordinal);

        return methods.Where(method => method.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal ||
                method.DeclaredAccessibility == Accessibility.Private && callbackNames.Contains(method.Name))
            .Select(method =>
            {
                var signature = CallbackSignature(method);
                var privateMember = method.DeclaredAccessibility == Accessibility.Private ? "1" : "0";
                var kinds = string.Join(",", CallbackKinds.Where(kind => SupportsCallback(method, kind.Context)).Select(kind => kind.Kind));
                return $"{method.Name}|{privateMember}|{kinds}|{signature}";
            }).ToArray();
    }

    private IEnumerable<CallbackCandidate> CallbackCandidates(string kind, string context)
    {
        var depth = 0;
        for (INamedTypeSymbol? owner = activity; owner is not null; owner = owner.BaseType, depth++)
        {
            var bridge = Same(owner.ContainingAssembly, compilation.Assembly) ? null : FindProducerBridge(compilation, owner);
            var declarations = CallbackLookupMethods(owner).Select(method =>
            {
                var bind = SupportsCallback(method, context);
                return new CallbackCandidate(method.Name, CallbackSignature(method), depth,
                    method.DeclaredAccessibility == Accessibility.Private, bind, method,
                    bind && method.DeclaredAccessibility != Accessibility.Public
                        ? CallbackBridgeCall(bridge, kind, method.Name, CallbackSignature(method.OriginalDefinition))
                        : null);
            }).ToArray();
            foreach (var candidate in declarations)
                yield return candidate;

            if (bridge is null)
                continue;

            // Project references can omit private and internal overloads. The declaring producer
            // exports the shape of those methods so a consumer can apply the same lookup scope.
            var seen = declarations.ToLookup(candidate => (candidate.Name, candidate.IsPrivate, candidate.Signature));
            var found = new Dictionary<(string, bool, string), int>();
            foreach (var field in bridge.GetMembers().OfType<IFieldSymbol>().Where(field =>
                         field.Name.StartsWith(CallbackLookupPrefix, StringComparison.Ordinal) && field.ConstantValue is string))
            {
                var metadata = ((string)field.ConstantValue!).Split('|');
                if (metadata.Length != 4)
                    throw new BindingException("callback metadata", $"Rebuild the declaring producer '{owner.ContainingAssembly.Name}'");
                var name = metadata[0];
                var isPrivate = metadata[1] == "1";
                var signature = SpecializeCallbackSignature(metadata[3], owner);
                var key = (name, isPrivate, signature);
                found.TryGetValue(key, out var count);
                found[key] = count + 1;
                if (count < seen[key].Count())
                    continue;

                var invocation = metadata[2].Split(',').Contains(kind, StringComparer.Ordinal)
                    ? CallbackBridgeCall(bridge, kind, name, metadata[3]) : null;
                yield return new(name, signature, depth, isPrivate, invocation is not null, Bridge: invocation);
            }
        }
    }

    private static string? CallbackBridgeCall(INamedTypeSymbol? bridge, string kind, string name, string signature)
    {
        var candidate = kind + "_" + name;
        return bridge?.GetMembers(candidate).OfType<IMethodSymbol>().Any(method => method.Parameters.Length == 2) == true
            ? $"{TypeName(bridge)}.{candidate}(activity, {Quote(signature)})"
            : null;
    }

    private static string CallbackSignature(IMethodSymbol method)
    {
        var parameters = ContainingTypes(method.ContainingType).SelectMany(type => type.OriginalDefinition.TypeParameters).ToArray();
        return string.Join(";", method.Parameters.Select(parameter =>
            CallbackTypeKey(parameter.Type, parameters) + (parameter.RefKind == RefKind.None ? "" : "&")));
    }

    private static string SpecializeCallbackSignature(string signature, INamedTypeSymbol owner)
    {
        var arguments = ContainingTypes(owner).SelectMany(type => type.TypeArguments).ToArray();
        var parameters = ContainingTypes(owner).SelectMany(type => type.OriginalDefinition.TypeParameters).ToArray();
        for (var index = 0; index < arguments.Length; index++)
            signature = signature.Replace($"~{index}~", CallbackTypeKey(arguments[index], parameters));
        return signature;
    }

    private static string CallbackTypeKey(ITypeSymbol type, ITypeParameterSymbol[] parameters)
    {
        if (type is IDynamicTypeSymbol)
            return "System.Object";
        if (type is ITypeParameterSymbol parameter)
        {
            var index = Array.FindIndex(parameters, candidate => Same(candidate, parameter.OriginalDefinition));
            return index >= 0 ? $"~{index}~" : $"!!{parameter.Ordinal}";
        }
        if (type is IArrayTypeSymbol array)
            return $"{CallbackTypeKey(array.ElementType, parameters)}[{new string(',', array.Rank - 1)}]";
        if (type is INamedTypeSymbol named)
        {
            var arguments = ContainingTypes(named).SelectMany(owner => owner.TypeArguments).ToArray();
            var name = MetadataName(named.OriginalDefinition);
            return arguments.Length == 0 ? name : $"{name}<{string.Join(",", arguments.Select(argument => CallbackTypeKey(argument, parameters)))}>";
        }
        return TypeName(type);
    }
}
