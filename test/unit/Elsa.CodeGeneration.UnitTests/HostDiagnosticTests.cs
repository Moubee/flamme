using Elsa.CodeGeneration.Analyzers;
using Elsa.CodeGeneration.Generators;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.CodeGeneration.UnitTests;

public class HostDiagnosticTests
{
    [Test]
    public async Task GeneratedCompilerRejectsAuthoredExportBootstrapBeforeEmittingRuntimeComposition()
    {
        const string source = """
            using Elsa.Features.Models;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;

            namespace Elsa.CodeGeneration;
            internal static partial class ElsaHost
            {
                internal static partial IModule CreateModule(IServiceCollection services, CompositionPolicy policy);
                private static FeatureModuleExport[] GetModuleExports() => [];
            }
            """;
        var input = CompilerTest.CreateCompilation(("Host.cs", source)).AddReferences(
            MetadataReference.CreateFromFile(typeof(IModule).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        var result = CompilerTest.CreateDriver(true, new ElsaHostGenerator()).Run(input, out _).GetRunResult();
        var diagnostic = result.Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN013");
        await Assert.That(diagnostic.GetMessage()).Contains("services.CreateModule()");
        await Assert.That(diagnostic.Location.SourceSpan.Start).IsEqualTo(source.IndexOf("CreateModule(", StringComparison.Ordinal));
        await Assert.That(result.Results.Single().GeneratedSources.Any(s => s.HintName == "ElsaHost.Bootstrap.g.cs")).IsFalse();
    }

    [Test]
    public async Task MissingBuildSettingsReportsTheAuthoredHostIdentifier()
    {
        const string source = """
            namespace Elsa.CodeGeneration;

            internal static partial class ElsaHost;
            """;
        var input = CompilerTest.CreateCompilation(("Host.cs", source));
        var analysis = input.WithAnalyzers([new ElsaHostAnalyzer()]);
        var diagnostic = (await analysis.GetAnalyzerDiagnosticsAsync()).Single();

        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN001");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.SourceTree!.FilePath).IsEqualTo("Host.cs");
        var identifier = new TextSpan(source.IndexOf("ElsaHost", StringComparison.Ordinal), "ElsaHost".Length);
        await Assert.That(diagnostic.Location.SourceSpan).IsEqualTo(identifier);
    }
}
