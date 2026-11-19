using Flamme.Expressions.JavaScript.TypeDefinitions.Models;

namespace Flamme.Expressions.JavaScript.TypeDefinitions.Contracts;

/// <summary>
/// Returns a <see cref="TypeDefinition"/> from a given <see cref="Type"/>.
/// </summary>
public interface ITypeDescriber
{
    /// <summary>
    /// Returns a <see cref="TypeDefinition"/> from a given <see cref="Type"/>.
    /// </summary>
    TypeDefinition DescribeType(Type type);
}