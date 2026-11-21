using System.Text;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.ActivityGenerator;

namespace Flamme.ActivityGenerator;

internal sealed partial class ActivityEmitter
{
    private const string ActivityAttributeName = Attributes + "ActivityAttribute";

    private string? EmitRuntimeIdentity(StringBuilder source)
    {
        if (!RequiresRuntimeIdentity(activity))
            return null;

        var identity = "Identity" + row;
        var attribute = EffectiveActivityAttribute(activity);
        var derived = IsDerivedActivityAttribute(attribute);
        source.AppendLine($"    private static readonly (string? Namespace, string Name, string TypeName, int Version) {identity} = Create{identity}();");
        source.AppendLine($"    private static (string? Namespace, string Name, string TypeName, int Version) Create{identity}()");
        source.AppendLine("    {");
        if (derived)
            source.AppendLine($"        var metadata = (global::{ActivityAttributeName})({NewAttribute(attribute!)});");

        var ns = ActivityNamespace(activity);
        var namespaceLiteral = ns is null ? "null" : Quote(ns);
        var namespaceExpression = derived ? $"metadata.Namespace ?? {namespaceLiteral}" : namespaceLiteral;
        var versionExpression = derived ? "metadata.Version" : Constant(ActivityArgument(activity, "Version", "version") ?? 1);
        source.AppendLine($"        string? ns = {namespaceExpression};");
        source.AppendLine($"        var name = {RuntimeWireName(activity, derived ? "metadata" : null)};");
        source.AppendLine($"        return (ns, name, ns == null ? name : ns + \".\" + name, {versionExpression});");
        source.AppendLine("    }");
        return identity;
    }

    private static AttributeData? EffectiveActivityAttribute(INamedTypeSymbol type)
    {
        var attributes = GetInheritedAttributes(type)
            .Where(attribute => attribute.AttributeClass is { } attributeType && IsAssignableTo(attributeType, ActivityAttributeName))
            .ToArray();
        if (attributes.Length > 1)
            throw new BindingException("activity identity", $"Use a single activity identity attribute on '{TypeName(type)}'.");

        return attributes.FirstOrDefault();
    }

    private static bool IsDerivedActivityAttribute(AttributeData? attribute) =>
        attribute?.AttributeClass is { } type && !Is(type, ActivityAttributeName);

    private static bool RequiresRuntimeIdentity(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return false;
        return IsDerivedActivityAttribute(EffectiveActivityAttribute(named)) ||
               ContainingTypes(named).SelectMany(owner => owner.TypeArguments).Any(RequiresRuntimeIdentity);
    }

    private string RuntimeWireName(ITypeSymbol type, string? metadata = null)
    {
        if (type is not INamedTypeSymbol named)
            return Quote(WireName(type));

        var attribute = EffectiveActivityAttribute(named);
        if (metadata is null && IsDerivedActivityAttribute(attribute))
            metadata = $"((global::{ActivityAttributeName})({NewAttribute(attribute!)}))";

        var name = metadata is null
            ? Quote(ActivityArgument(named, "Type", "type") as string ?? named.Name)
            : $"({metadata}.Type ?? {Quote(named.Name)})";
        var arguments = ContainingTypes(named).SelectMany(owner => owner.TypeArguments).ToArray();
        if (arguments.Length == 0)
            return name;

        return name + " + \"<\" + " + string.Join(" + \",\" + ", arguments.Select(argument => RuntimeWireName(argument))) + " + \">\"";
    }
}
