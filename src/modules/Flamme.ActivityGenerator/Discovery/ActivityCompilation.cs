using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.Discovery;

/// <summary>Shares full producer metadata and semantic models between discovery and the module writer.</summary>
internal sealed class ActivityCompilation
{
    private static readonly ConditionalWeakTable<Compilation, ActivityCompilation> Compilations = new();
    private readonly ConditionalWeakTable<SyntaxTree, SemanticModel> semanticModels = new();

    private ActivityCompilation(Compilation compilation)
    {
        Compilation = compilation;
        if (compilation.GetTypeByMetadataName(Workflows + "ActivityContext") is null ||
            compilation.GetTypeByMetadataName(Workflows + "IActivity") is null)
            return;

        // Imported private members and constructors can affect both overload resolution and generated bindings.
        Compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
        Activity = Compilation.GetTypeByMetadataName(Workflows + "IActivity");
        ContextType = Compilation.GetTypeByMetadataName(Workflows + "ActivityContext");
    }

    public Compilation Compilation { get; }
    public INamedTypeSymbol? Activity { get; }
    public INamedTypeSymbol? ContextType { get; }

    public static ActivityCompilation Get(Compilation compilation) =>
        Compilations.GetValue(compilation, static key => new ActivityCompilation(key));

    public SemanticModel GetSemanticModel(SyntaxTree tree) =>
        semanticModels.TryGetValue(tree, out var model)
            ? model : semanticModels.GetValue(tree, key => Compilation.GetSemanticModel(key));
}
