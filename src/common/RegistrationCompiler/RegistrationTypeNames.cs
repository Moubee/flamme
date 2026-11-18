using System.Text;
using Microsoft.CodeAnalysis;

namespace RegistrationCompiler;

/// <summary>Renders fully qualified C# type names in a generated file's own extern-alias scope.</summary>
public sealed class RegistrationTypeNames
{
    private readonly Dictionary<IAssemblySymbol, string> _aliases = new(SymbolEqualityComparer.Default);
    private readonly SortedSet<string> _externAliases = new(StringComparer.Ordinal);

    public RegistrationTypeNames(Compilation compilation)
    {
        // Most compilations use only the global namespace. Resolve symbols only for aliased references.
        foreach (var reference in compilation.References)
        {
            var aliases = reference.Properties.Aliases;
            if (aliases.IsDefaultOrEmpty || aliases.Contains("global")
                || compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;
            var alias = "@" + aliases.OrderBy(name => name, StringComparer.Ordinal).First();
            if (!_aliases.TryGetValue(assembly, out var previous) || string.CompareOrdinal(alias, previous) < 0)
                _aliases[assembly] = alias;
        }
        // Prefer a separate global reference to the same assembly, when one exists.
        if (_aliases.Count > 0)
        {
            foreach (var reference in compilation.References)
                if ((reference.Properties.Aliases.IsDefaultOrEmpty || reference.Properties.Aliases.Contains("global"))
                    && compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                    _aliases.Remove(assembly);
        }
    }

    /// <summary>File-scope directives for aliases used by calls to <see cref="Format"/>.</summary>
    public IEnumerable<string> ExternAliases => _externAliases;

    public string Format(ITypeSymbol type)
    {
        if (_aliases.Count == 0)
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // The display parts retain the symbols inside constructed types, arrays and tuples.
        // Each global:: prefix belongs to the first named type following that prefix.
        var parts = type.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat);
        var name = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var text = parts[i].ToString();
            if (text == "global" && i + 1 < parts.Length && parts[i + 1].ToString() == "::")
            {
                for (var j = i + 2; j < parts.Length; j++)
                {
                    if (parts[j].Symbol is not INamedTypeSymbol named)
                        continue;
                    if (_aliases.TryGetValue(named.ContainingAssembly, out var alias))
                    {
                        text = alias;
                        _externAliases.Add("extern alias " + alias + ";");
                    }
                    break;
                }
            }
            name.Append(text);
        }
        return name.ToString();
    }
}
