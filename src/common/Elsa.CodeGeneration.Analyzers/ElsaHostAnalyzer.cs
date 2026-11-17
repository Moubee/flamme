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
    private static readonly DiagnosticDescriptor Host = Descriptor("ELSAGEN001", "Declare the Elsa host contract");
    private static readonly DiagnosticDescriptor Entry = Descriptor("ELSAGEN002", "Declare the selected host bootstrap entry");
    private static readonly DiagnosticDescriptor Hook = Descriptor("ELSAGEN003", "Declare the selected host hook");
    private static readonly DiagnosticDescriptor Manual = Descriptor("ELSAGEN004", "Select the matching manual bootstrap hook");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Host, Entry, Hook, Manual);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(static analysis =>
        {
            var settings = CompilerSettings.Read(analysis.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
            if (!settings.IsHost)
                return;
            var result = CompilerContract.Validate(analysis.Compilation, settings, analysis.CancellationToken);
            if (result.Success)
                return;
            var descriptor = result.Failure switch
            {
                HostFailure.Entry => Entry,
                HostFailure.Hook => Hook,
                HostFailure.ManualSelection => Manual,
                _ => Host
            };
            analysis.ReportDiagnostic(Diagnostic.Create(descriptor, result.Location, result.Message));
        });
    }

    private static DiagnosticDescriptor Descriptor(string id, string title) => new(id, title, "{0}", "Elsa.CodeGeneration", DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "Elsa host bootstrap selection is explicit at compile time. Generated and manual hosts use the same typed runtime contracts.");
}