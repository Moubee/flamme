using System.Text;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.ActivityGenerator;

namespace Flamme.ActivityGenerator;

internal sealed partial class ActivityEmitter
{
    private string? EmitServiceProviderActivation(StringBuilder source)
    {
        if (!IsAssignableTo(activity, Workflows + "IActivityPropertyDefaultValueProvider"))
            return null;

        const string activation = "global::Flamme.Workflows.ActivityProviderActivation";
        const string di = "Microsoft.Extensions.DependencyInjection.";
        source.AppendLine($"    private static readonly {activation}.Constructor[] {row}ProviderConstructors = [");

        foreach (var constructor in activity.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public))
        {
            var parameters = constructor.Parameters;
            if (parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType ||
                    parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
                    !compilation.IsSymbolAccessibleWithin(parameter.Type, compilation.Assembly)))
                throw new BindingException("default provider constructor", "Use accessible ordinary service parameters.");

            var preferred = FindAttribute(constructor, di + "ActivatorUtilitiesConstructorAttribute") is not null;
            var arguments = parameters.Select((parameter, index) => $"({TypeName(parameter.Type)})arguments[{index}]!").ToArray();
            source.AppendLine($"        new({Boolean(preferred)}, [");
            foreach (var parameter in parameters)
            {
                var keyed = FindAttribute(parameter, di + "FromKeyedServicesAttribute");
                var key = keyed is { ConstructorArguments.Length: > 0 } ? Constant(keyed.ConstructorArguments[0]) : "null";
                var defaultValue = parameter.HasExplicitDefaultValue ? DefaultArgument(parameter) : "null";
                source.AppendLine($"            new({TypeOf(parameter.Type)}, {key}, {Boolean(parameter.HasExplicitDefaultValue)}, {defaultValue}),");
            }
            source.AppendLine($"        ], static arguments => {Construct(constructor, arguments)}),");
        }
        source.AppendLine("    ];");

        return $"static services => {activation}.Create(services, {TypeOf(activity)}, {row}ProviderConstructors)";
    }

    private string Construct(IMethodSymbol ctor, string[] args)
    {
        var required = Hierarchy(activity).Any(type => type.GetMembers().Any(member =>
            member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }));
        if (Accessible(ctor) && (!required || FindAttribute(ctor, "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute") is not null))
            return $"new {TypeName(activity)}({string.Join(", ", args)})";
        return CallAccessor(ctor, "", args);
    }

    private static string DefaultArgument(IParameterSymbol p)
    {
        if (!p.HasExplicitDefaultValue || p.ExplicitDefaultValue is null)
            return $"default({TypeName(p.Type)})";
        return $"({TypeName(p.Type)})({Constant(p.ExplicitDefaultValue)})";
    }
}
