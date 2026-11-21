using System.Text;
using Flamme.ActivityGenerator.Diagnostics;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.CodeGeneration;

internal sealed partial class ActivityEmitter
{
    private static bool IsBoundProperty(IPropertySymbol property) =>
        IsAssignableTo(property.Type, Input) || IsAssignableTo(property.Type, Output) ||
        FindAttribute(property, InputAttribute) is not null ||
        !property.IsStatic && (IsAssignableTo(property.Type, ActivityInterface) || IsActivityCollection(property.Type));

    private IEnumerable<IPropertySymbol> GetProperties()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in Hierarchy(activity))
        foreach (var property in owner.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic && !Same(owner, activity))
                continue;
            var key = property.MetadataName + ":" + TypeName(property.Type) + ":" + property.IsStatic +
                "(" + string.Join(",", property.Parameters.Select(parameter => TypeName(parameter.Type))) + ")";
            if (seen.Add(key))
                yield return property;
        }
    }

    private Property Bind(IPropertySymbol property)
    {
        if (property.IsIndexer || property.RefKind != RefKind.None || property.Type.IsRefLikeType ||
            property.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
            !compilation.IsSymbolAccessibleWithin(property.Type, compilation.Assembly))
            throw new BindingException("property " + property.Name, "Use an accessible property type with an ordinary getter and setter.",
                SourceLocations(property));

        var name = Quote(property.Name);
        var owner = TypeName(property.ContainingType);
        var instance = property.IsStatic ? owner : ActivityAccessorWriter.InstanceTarget(property.ContainingType, "a");
        var member = instance + "." + Identifier(property.Name);
        var value = property.Type.IsValueType ? $"(object?)v is null ? default : ({TypeName(property.Type)})v" : $"({TypeName(property.Type)})v!";
        var read = property.GetMethod is not { } getter ? null : Accessible(getter)
            ? member
            : accessors.Call(getter, instance, []);
        var write = property.SetMethod is not { } setter ? null : Accessible(setter) && !setter.IsInitOnly
            ? $"{member} = {value}"
            : accessors.Call(setter, instance, [value]);

        // Reflection wrapped exceptions thrown *inside* public or nonpublic accessors.
        if (!Same(property.ContainingAssembly, compilation.Assembly) && ActivityCallbackWriter.FindProducerBridge(compilation, property.ContainingType) is { } bridge)
        {
            var ownerGet = "Get_" + property.Name;
            var ownerSet = "Set_" + property.Name;
            if (bridge.GetMembers(ownerGet).Length != 0)
                read = $"({TypeName(property.Type)}){TypeName(bridge)}.{ownerGet}((IActivity)a)!";
            if (bridge.GetMembers(ownerSet).Length != 0)
                write = $"{TypeName(bridge)}.{ownerSet}((IActivity)a, v)";
        }

        var runtimeGetter = read is null ? $"static a => throw new ArgumentException(\"Property getter not found. \" + {name})"
            : $"static a => {{ try {{ return {read}; }} catch (Exception e) {{ throw new TargetInvocationException(e); }} }}";
        var valueType = NormalizeNullable(property.Type);
        var invalidValue = $"if (v is not null && v is not {TypeName(valueType)}) " +
            $"throw new ArgumentException($\"Object of type '{{v.GetType()}}' cannot be converted to type '{{{TypeOf(property.Type)}}}'.\");";
        var runtimeSetter = write is null ? $"static (a, v) => throw new ArgumentException(\"Property setter not found. \" + {name})"
            : $"static (a, v) => {{ {invalidValue} try {{ {write}; }} catch (Exception e) {{ throw new TargetInvocationException(e); }} }}";

        return new(property, runtimeGetter, runtimeSetter, !property.IsStatic && IsAssignableTo(property.Type, ActivityInterface),
            !property.IsStatic && IsActivityCollection(property.Type));
    }

    private static bool IsActivityCollection(ITypeSymbol type) => type.AllInterfaces.Prepend(type)
        .OfType<INamedTypeSymbol>().Any(iface => Is(iface, "System.Collections.Generic.IEnumerable`1") &&
            IsAssignableTo(iface.TypeArguments[0], ActivityInterface));

    private void EmitMember(StringBuilder source, Property property, string id, bool isInput)
    {
        var symbol = property.Symbol;
        source.AppendLine($"    private static readonly ActivityMemberBinding {row}{id} = Create{row}{id}();");
        source.AppendLine($"    private static ActivityMemberBinding Create{row}{id}() =>");
        source.Append($"        new({Quote(symbol.Name)}, {TypeOf(symbol.ContainingType)}, {TypeOf(symbol.Type)}, {property.Getter}, {property.Setter})");
        if (isInput && IsAssignableTo(symbol.Type, Input))
        {
            var wrapped = GetWrapper(symbol.Type, Input);
            if (wrapped is null || !Same(wrapped, symbol.Type))
                throw new BindingException("omitted input " + symbol.Name, "Declare the property as Input<T> to supply a typed default factory.",
                    SourceLocations(symbol));
            // Omitted defaults historically use the normalized descriptor type. Preserve
            // the incompatible-wrapper error for an omitted non-null nullable value default.
            var valueType = NormalizeNullable(wrapped.TypeArguments[0]);
            source.Append($" {{ CreateInput = static e => new global::{Input}<{TypeName(valueType)}>(e) }}");
        }
        source.AppendLine(";");
    }

    private void EmitPorts(StringBuilder source, Property[] properties)
    {
        source.AppendLine($"    private static IEnumerable<ActivityPort> Get{row}Ports(IActivity activity)");
        source.AppendLine("    {");
        source.AppendLine("        var ports = new List<ActivityPort>();");
        var portIndex = 0;
        foreach (var property in properties.Where(property => property.Child || property.ChildCollection))
        {
            var kind = property.ChildCollection ? "Activities" : "Activity";
            var type = property.ChildCollection ? "IEnumerable<IActivity>" : "IActivity";
            source.AppendLine($"        if (((Func<IActivity, object?>)({property.Getter}))(activity) is {type} port{portIndex})");
            source.AppendLine($"            ports.Add(ActivityPort.From{kind}(port{portIndex}, {Quote(property.Symbol.Name)}));");
            portIndex++;
        }
        source.AppendLine("        return ports;");
        source.AppendLine("    }");
    }

    private static ITypeSymbol NormalizeNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? nullable.TypeArguments[0]
            : type;

    private static INamedTypeSymbol? GetWrapper(ITypeSymbol type, string name) => type is INamedTypeSymbol named
        ? Hierarchy(named).FirstOrDefault(candidate => Is(candidate, name + "`1")) : null;
}
