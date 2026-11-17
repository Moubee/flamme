using System.Collections.Immutable;
using Elsa.CodeGeneration.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Elsa.CodeGeneration.Analyzers;

/// <summary>
/// Validates only the selected authored bootstrap and leaves runtime export coverage to the catalog.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ElsaHostAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => CompilerDiagnostics.All;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationAction(static analysis =>
        {
            var settings = CompilerSettings.Read(analysis.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
            // Compiler-owned partial declarations must be diagnosed by the generator: declaration
            // pairing errors can prevent the compiler from running diagnostic analyzers.
            if (!settings.HasIsHost)
            {
                var authored = analysis.Compilation.GetTypeByMetadataName(CompilerContract.HostMetadataName);
                if (authored is not null && authored.DeclaringSyntaxReferences.Length > 0
                    && SymbolEqualityComparer.Default.Equals(authored.ContainingAssembly, analysis.Compilation.Assembly))
                {
                    var missingSettings = CompilerContract.Validate(analysis.Compilation, settings, analysis.CancellationToken);
                    analysis.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.For(missingSettings.Failure), missingSettings.Location, missingSettings.Message));
                }
                return;
            }
            if (!settings.IsHost || !settings.IsManual)
                return;
            var result = CompilerContract.Validate(analysis.Compilation, settings, analysis.CancellationToken);
            if (result.Success)
                return;
            analysis.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.For(result.Failure), result.Location, result.Message));
        });
    }

}