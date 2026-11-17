using Elsa.CodeGeneration.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.CodeGeneration.UnitTests;

public class HostDiagnosticTests
{
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
