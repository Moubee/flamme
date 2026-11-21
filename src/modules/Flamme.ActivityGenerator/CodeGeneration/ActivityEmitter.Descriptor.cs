using System.Text;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.CodeGeneration;

internal sealed partial class ActivityEmitter
{
    private void EmitDescriptor(StringBuilder source, Property[] inputs, Property[] outputs, Property[] properties, string? ns,
        string name, string fullName, string version, string? identity)
    {
        source.AppendLine($"    private static ActivityDescriptor Describe{row}(IPropertyDefaultValueResolver defaults)");
        source.AppendLine("    {");
        var attributes = string.Join(", ", GetInheritedAttributes().Select(NewAttribute));
        source.AppendLine($"        Attribute[] attributes = [{attributes}];");
        var descriptorIdentity = identity is null
            ? $"{Quote(fullName)}, {Quote(name)}, {Quote(ns ?? string.Empty)}, {version}"
            : $"{identity}.TypeName, {identity}.Name, {identity}.Namespace ?? string.Empty, {identity}.Version";
        source.AppendLine($"        var descriptor = GeneratedActivityDescriptorFactory.Create({TypeOf(activity)}, {descriptorIdentity}, " +
            $"{Quote(FriendlyName(activity))}, " +
            $"{Boolean(IsAssignableTo(activity, Workflows + "ITrigger"))}, {Boolean(IsAssignableTo(activity, Workflows + "Activities.Container"))}, " +
            $"{Boolean(IsAssignableTo(activity, Workflows + "IStartNode"))}, {Boolean(IsAssignableTo(activity, Workflows + "ITerminalNode"))}, attributes);");

        for (var i = 0; i < inputs.Length; i++)
            EmitInput(source, inputs[i].Symbol, i);
        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < outputs.Length; i++)
            if (outputNames.Add(outputs[i].Symbol.Name))
                EmitOutput(source, outputs[i].Symbol, i);
        foreach (var property in properties.Where(property => property.Child))
        {
            var symbol = property.Symbol;
            var port = AttributeExpression(symbol, Attributes + "PortAttribute");
            var browsable = port == "null" ? "null" : AttributeExpression(symbol, "System.ComponentModel.BrowsableAttribute");
            source.AppendLine($"        GeneratedActivityDescriptorFactory.AddEmbeddedPort(descriptor, {Quote(symbol.Name)}, {port}, {browsable});");
        }

        source.AppendLine("        GeneratedActivityDescriptorFactory.AddFlowPorts(descriptor, attributes);");
        source.AppendLine("        return descriptor;");
        source.AppendLine("    }");
    }

    private void EmitInput(StringBuilder source, IPropertySymbol property, int index)
    {
        var wrapped = GetWrapper(property.Type, Input);
        var propertyType = NormalizeNullable(wrapped?.TypeArguments[0] ?? property.Type);
        var input = AttributeExpression(property, InputAttribute);
        var description = AttributeExpression(property, "System.ComponentModel.DescriptionAttribute");
        source.AppendLine($"        GeneratedActivityDescriptorFactory.AddInput(descriptor, defaults, {row}Input{index}, " +
            $"{TypeOf(propertyType)}, {Boolean(wrapped is not null)}, {input}, {description});");
    }

    private void EmitOutput(StringBuilder source, IPropertySymbol property, int index)
    {
        var wrapper = GetWrapper(property.Type, Output);
        var propertyType = wrapper is null ? "typeof(object)" : TypeOf(wrapper.TypeArguments[0]);
        var output = AttributeExpression(property, OutputAttribute);
        var description = AttributeExpression(property, "System.ComponentModel.DescriptionAttribute");
        source.AppendLine($"        GeneratedActivityDescriptorFactory.AddOutput(descriptor, {row}Output{index}, " +
            $"{propertyType}, {output}, {description}, attributes);");
    }

    private string AttributeExpression(ISymbol member, string attributeName) =>
        FindAttribute(member, attributeName) is { } attribute ? NewAttribute(attribute) : "null";
}
