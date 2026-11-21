using System.Text;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.CodeGeneration;

internal sealed partial class ActivityEmitter(
    Compilation compilation, INamedTypeSymbol activity, string row, bool inferred = false)
{
    private const string ActivityInterface = Workflows + "IActivity";
    private const string InputAttribute = Attributes + "InputAttribute";
    private const string OutputAttribute = Attributes + "OutputAttribute";
    private const string Input = ModelsNamespace + "Input";
    private const string Output = ModelsNamespace + "Output";

    private readonly ActivityAccessorWriter accessors = new(row);

    private sealed record Property(IPropertySymbol Symbol, string Getter, string Setter,
        bool Child, bool ChildCollection);

    public void Emit(StringBuilder source)
    {
        var publicProperties = GetProperties().ToArray();
        var properties = publicProperties.Where(IsBoundProperty).Select(Bind).ToArray();
        var inputs = properties
            .Where(property => IsAssignableTo(property.Symbol.Type, Input) || FindAttribute(property.Symbol, InputAttribute) is not null)
            .GroupBy(property => property.Symbol.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var outputs = properties.Where(property => IsAssignableTo(property.Symbol.Type, Output)).ToArray();
        var ns = ActivityNamespace(activity);
        var name = WireName(activity);
        var fullName = ns is null ? name : ns + "." + name;
        var version = Constant(ActivityArgument(activity, "Version", "version") ?? 1);
        var identity = EmitRuntimeIdentity(source);
        var defaultsFactory = EmitServiceProviderActivation(source);
        new ActivityCallbackWriter(compilation, activity, row, accessors).WriteBindings(source);

        for (var i = 0; i < inputs.Length; i++)
            EmitMember(source, inputs[i], "Input" + i, true);
        for (var i = 0; i < outputs.Length; i++)
            EmitMember(source, outputs[i], "Output" + i, false);

        EmitPorts(source, properties);
        EmitDescriptor(source, inputs, outputs, properties, ns, name, fullName, version, identity);

        var propertyNames = publicProperties.Where(property => !property.IsStatic).Select(property => Quote(property.Name)).Distinct();
        source.AppendLine($"    internal static readonly GeneratedActivityBinding {row} = Create{row}();");
        source.AppendLine($"    private static GeneratedActivityBinding Create{row}() => new()");
        source.AppendLine("    {");
        source.AppendLine($"        ActivityType = {TypeOf(activity)},");
        source.AppendLine($"        Namespace = {(identity is null ? (ns is null ? "null" : Quote(ns)) : $"{identity}.Namespace")},");
        source.AppendLine($"        Name = {(identity is null ? Quote(name) : $"{identity}.Name")},");
        source.AppendLine($"        TypeName = {(identity is null ? Quote(fullName) : $"{identity}.TypeName")},");
        source.AppendLine($"        Version = {(identity is null ? version : $"{identity}.Version")},");
        source.AppendLine($"        IsInferred = {Boolean(inferred)},");
        source.AppendLine($"        CreateDescriptor = Describe{row},");
        if (defaultsFactory is not null)
            source.AppendLine($"        CreateDefaultValueProvider = {defaultsFactory},");
        source.AppendLine($"        Inputs = Array.AsReadOnly<ActivityMemberBinding>([{MemberNames("Input", inputs.Length)}]),");
        source.AppendLine($"        Outputs = Array.AsReadOnly<ActivityMemberBinding>([{MemberNames("Output", outputs.Length)}]),");
        source.AppendLine($"        PublicPropertyNamesFactory = Create{row}PublicPropertyNames,");
        source.AppendLine($"        GetActivityPorts = Get{row}Ports,");
        source.AppendLine($"        BindBookmarkCallback = Bind{row}Bookmark,");
        source.AppendLine($"        BindCompletionCallback = Bind{row}Completion,");
        source.AppendLine($"        GetOutcomeName = Get{row}OutcomeName,");
        source.AppendLine("    };");

        source.AppendLine($"    private static string[] Create{row}PublicPropertyNames() => [{string.Join(", ", propertyNames)}];");
        source.AppendLine($"    private static string Get{row}OutcomeName(string name) => name switch");
        source.AppendLine("    {");
        foreach (var property in properties.Where(property => property.Child).GroupBy(property => property.Symbol.Name).Select(group => group.First()))
        {
            var outcome = FindAttribute(property.Symbol, Attributes + "PortAttribute");
            source.AppendLine($"        {Quote(property.Symbol.Name)} => {PortOutcomeName(outcome)} ?? name,");
        }
        source.AppendLine("        _ => throw new InvalidOperationException($\"Activity does not have a child port named '{name}'.\"),");
        source.AppendLine("    };");
        accessors.WriteDeclarations(source);
    }

    public void EmitProducerCallbacks(StringBuilder source)
    {
        var properties = activity.GetMembers().OfType<IPropertySymbol>().Where(property =>
            property.DeclaredAccessibility == Accessibility.Public && !property.IsIndexer && !property.IsStatic && IsBoundProperty(property));
        new ActivityCallbackWriter(compilation, activity, row, accessors, isProducerBridge: true).WriteProducerBridge(source, properties);
    }

    private string MemberNames(string kind, int count) => string.Join(", ", Enumerable.Range(0, count).Select(index => row + kind + index));

    private bool Accessible(ISymbol symbol) => compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);
}
