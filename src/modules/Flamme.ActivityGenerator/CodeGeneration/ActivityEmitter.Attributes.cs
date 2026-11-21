using System.Globalization;
using Flamme.ActivityGenerator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.CodeGeneration;

internal sealed partial class ActivityEmitter
{
    private string ActivityAttributeList()
    {
        var attributes = GetInheritedAttributes(activity).ToArray();
        if (attributes.Any(attribute => !CanConstructAttribute(attribute)))
            return $"GeneratedActivityDescriptorFactory.ReadActivityAttributes({TypeOf(activity)})";

        return $"[{string.Join(", ", attributes.Select(NewAttribute))}]";
    }

    private static IEnumerable<AttributeData> GetInheritedAttributes(INamedTypeSymbol symbol)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var owner in Hierarchy(symbol))
        foreach (var attribute in owner.GetAttributes())
        {
            if (attribute.AttributeClass is not { } type || !IsEmitted(attribute))
                continue;

            var usage = GetAttributeUsage(type);
            if (!Same(owner, symbol) && NamedValue(usage, "Inherited", true) is false)
                continue;
            if (!seen.Add(type) && NamedValue(usage, "AllowMultiple", false) is false)
                continue;
            yield return attribute;
        }
    }

    private static AttributeData? GetAttributeUsage(INamedTypeSymbol type) =>
        Hierarchy(type).SelectMany(owner => owner.GetAttributes())
            .FirstOrDefault(attribute => Is(attribute.AttributeClass, "System.AttributeUsageAttribute"));

    private static AttributeData? FindAttribute(ISymbol symbol, string name)
    {
        // CLR attribute lookup on a property follows virtual overrides. A new/hidden
        // declaration has no OverriddenProperty and starts a separate metadata contract.
        if (symbol is IPropertySymbol property && InheritsPropertyMetadata(name))
        {
            AttributeData? found = null;
            for (var current = property; current != null; current = current.OverriddenProperty)
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass is not { } type || !IsAssignableTo(type, name) || !IsEmitted(attribute))
                    continue;

                var usage = GetAttributeUsage(type);
                if (!Same(current, property) && NamedValue(usage, "Inherited", true) is false)
                    continue;
                if (found is null)
                    found = attribute;
                else if (!Same(found.AttributeClass, type) || NamedValue(usage, "AllowMultiple", false) is true)
                    throw new BindingException($"property '{property.Name}' metadata", $"Declare one effective '{name}' attribute.");
            }

            return found;
        }

        return symbol.GetAttributes().FirstOrDefault(attribute => Is(attribute.AttributeClass, name) && IsEmitted(attribute));
    }

    private static bool InheritsPropertyMetadata(string name) => name is
        InputAttribute or OutputAttribute or Attributes + "PortAttribute" or
        "System.ComponentModel.DescriptionAttribute" or "System.ComponentModel.BrowsableAttribute";

    private string PortOutcomeName(IPropertySymbol property)
    {
        var portName = Attributes + "PortAttribute";
        var attribute = FindAttribute(property, portName);
        if (attribute is null)
            return "null";

        if (!Is(attribute.AttributeClass, portName))
            return $"((global::{portName})({PropertyAttributeExpression(property, portName, attribute)})).Name";

        return Constant(NamedValue(attribute, "Name", attribute.ConstructorArguments.FirstOrDefault().Value));
    }

    private static bool IsEmitted(AttributeData attribute)
    {
        // Metadata attributes are already filtered by the compiler that built the producer.
        var syntax = attribute.ApplicationSyntaxReference;
        if (syntax is null || attribute.AttributeClass is not { } type)
            return true;
        var conditions = Hierarchy(type).SelectMany(owner => owner.GetAttributes())
            .Where(meta => Is(meta.AttributeClass, "System.Diagnostics.ConditionalAttribute"))
            .Select(meta => meta.ConstructorArguments[0].Value).OfType<string>().ToArray();
        if (conditions.Length == 0)
            return true;

        var tree = syntax.SyntaxTree;
        var defined = new HashSet<string>(((CSharpParseOptions)tree.Options).PreprocessorSymbolNames, StringComparer.Ordinal);
        foreach (var trivia in tree.GetRoot().DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.SpanStart >= syntax.Span.Start)
                break;
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } directive:
                    defined.Add(directive.Name.ValueText);
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } directive:
                    defined.Remove(directive.Name.ValueText);
                    break;
            }
        }
        return conditions.Any(defined.Contains);
    }

    private static object? NamedValue(AttributeData? attribute, string property, object? fallback = null)
    {
        if (attribute is not null)
            foreach (var pair in attribute.NamedArguments)
                if (pair.Key == property)
                    return pair.Value.Value;
        return fallback;
    }

    private bool CanConstructAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } type && CanNameAttributeType(type) &&
        attribute.AttributeConstructor is { } constructor && Accessible(constructor) &&
        constructor.Parameters.All(parameter => CanNameAttributeType(parameter.Type)) &&
        attribute.ConstructorArguments.All(CanEmitConstant) &&
        attribute.NamedArguments.All(pair => CanEmitConstant(pair.Value));

    private bool CanNameAttributeType(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => CanNameAttributeType(array.ElementType),
        INamedTypeSymbol named => !named.IsFileLocal && Accessible(named) &&
            (named.ContainingType is null || CanNameAttributeType(named.ContainingType)) &&
            (named.IsUnboundGenericType || named.TypeArguments.All(CanNameAttributeType)),
        _ => Accessible(type)
    };

    private bool CanEmitConstant(TypedConstant constant) =>
        (constant.Type is null || CanNameAttributeType(constant.Type)) &&
        (constant.Kind == TypedConstantKind.Array
            ? constant.IsNull || constant.Values.All(CanEmitConstant)
            : constant.Value is not ITypeSymbol type || CanNameAttributeType(type));

    private static string NewAttribute(AttributeData attribute)
    {
        var type = attribute.AttributeClass!;
        var constructor = attribute.AttributeConstructor!;
        var arguments = attribute.ConstructorArguments.Select((argument, index) =>
            $"({TypeName(constructor.Parameters[index].Type)})({Constant(argument)})");
        var initializer = attribute.NamedArguments.Length == 0 ? "" : " { " +
            string.Join(", ", attribute.NamedArguments.Select(pair => $"{Identifier(pair.Key)} = {Constant(pair.Value)}")) + " }";
        return $"new {TypeName(type)}({string.Join(", ", arguments)}){initializer}";
    }

    private static string Constant(TypedConstant constant)
    {
        if (constant.IsNull)
            return "null";
        if (constant.Kind == TypedConstantKind.Array && constant.Type is IArrayTypeSymbol array)
            return $"new {TypeName(array.ElementType)}[] {{ {string.Join(", ", constant.Values.Select(Constant))} }}";
        if (constant.Kind == TypedConstantKind.Enum)
            return $"({TypeName(constant.Type!)})({Constant(constant.Value)})";
        return Constant(constant.Value);
    }

    private static string Constant(object? value) => value switch
    {
        null => "null",
        string text => Quote(text),
        bool flag => Boolean(flag),
        char character => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(character, true),
        ITypeSymbol type => TypeOf(type),
        float number when float.IsNaN(number) => "float.NaN",
        float number when float.IsPositiveInfinity(number) => "float.PositiveInfinity",
        float number when float.IsNegativeInfinity(number) => "float.NegativeInfinity",
        float number => number.ToString("R", CultureInfo.InvariantCulture) + "F",
        double number when double.IsNaN(number) => "double.NaN",
        double number when double.IsPositiveInfinity(number) => "double.PositiveInfinity",
        double number when double.IsNegativeInfinity(number) => "double.NegativeInfinity",
        double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
        uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
        byte number => "(byte)" + number.ToString(CultureInfo.InvariantCulture),
        sbyte number => "(sbyte)" + number.ToString(CultureInfo.InvariantCulture),
        short number => "(short)" + number.ToString(CultureInfo.InvariantCulture),
        ushort number => "(ushort)" + number.ToString(CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new BindingException("constant metadata", "Use a supported attribute constant.")
    };
}
