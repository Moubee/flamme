using System.Collections.Immutable;
using Flamme.ActivityGenerator.CodeGeneration;
using Flamme.ActivityGenerator.Discovery;
using Flamme.ActivityGenerator.Models;
using Microsoft.CodeAnalysis;

namespace Flamme.ActivityGenerator;

[Generator]
public sealed class ActivityGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = ActivityDiscovery.CreateCandidates(context.SyntaxProvider).Collect();
        var modules = context.CompilationProvider.Combine(candidates)
            .Select(static (input, token) => GenerateModule(input.Left, input.Right, token))
            .WithTrackingName("ActivityModule");

        var sources = modules.Select(static (module, _) => module.Source)
            .WithTrackingName("ActivityModuleSource");
        context.RegisterSourceOutput(sources, static (output, source) =>
        {
            if (source is not null)
                output.AddSource("Flamme.ActivityModule.g.cs", source);
        });

        var diagnostics = modules.SelectMany(static (module, _) => module.Diagnostics)
            .WithTrackingName("ActivityBindingDiagnostics");
        context.RegisterSourceOutput(diagnostics, static (output, diagnostic) => output.ReportDiagnostic(diagnostic));
    }

    private static ActivityGenerationResult GenerateModule(Compilation compilation, ImmutableArray<SyntaxNode> candidates,
        CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var discovery = ActivityDiscovery.Discover(compilation, candidates, cancellationToken, diagnostics.Add);
        var source = discovery is null ? null : ActivityModuleWriter.Write(discovery, diagnostics.Add, cancellationToken);
        return new ActivityGenerationResult(source, diagnostics.ToImmutable());
    }
}
