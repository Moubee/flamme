using System.Dynamic;
using Flamme.Extensions;
using Flamme.Expressions.JavaScript.TypeDefinitions.Abstractions;
using Flamme.Expressions.JavaScript.TypeDefinitions.Contracts;
using Flamme.Expressions.JavaScript.TypeDefinitions.Models;
using Flamme.Workflows.Management.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

namespace Flamme.Expressions.JavaScript.Providers;

/// <summary>
/// Produces <see cref="TypeDefinition"/>s for variable types.
/// </summary>
[UsedImplicitly]
internal class VariableTypeDefinitionProvider(ITypeDescriber typeDescriber, IOptions<ManagementOptions> options) : TypeDefinitionProvider
{
    protected override IEnumerable<TypeDefinition> GetTypeDefinitions(TypeDefinitionContext context)
    {
        var excludedTypes = new Func<Type, bool>[]
        {
            type => type == typeof(ExpandoObject),
            type => type.IsPrimitive,
            type => type.ContainsGenericParameters,
            type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>),
            type => type == typeof(object),
            type => type == typeof(string)
        };

        var variableTypes =
            from variableDescriptor in options.Value.VariableDescriptors
            let variableType = variableDescriptor.Type
            where (variableType.IsClass || variableType.IsInterface || variableType.IsEnum) && !excludedTypes.Any(x => x(variableType))
            select variableType;

        foreach (var variableType in variableTypes.Distinct())
        {
            yield return typeDescriber.DescribeType(variableType);
        }
    }
}