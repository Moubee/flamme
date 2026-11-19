using Flamme.Expressions.JavaScript.TypeDefinitions.Models;

namespace Flamme.Expressions.JavaScript.TypeDefinitions.Contracts;

/// <summary>
/// Renders a <see cref="TypeDefinitionsDocument"/> to a string.
/// </summary>
public interface ITypeDefinitionDocumentRenderer
{
    /// <summary>
    /// Renders a <see cref="TypeDefinitionsDocument"/> to a string.
    /// </summary>
    string Render(TypeDefinitionsDocument document);
}