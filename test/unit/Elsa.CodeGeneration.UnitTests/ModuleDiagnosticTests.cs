using Elsa.CodeGeneration.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.CodeGeneration.UnitTests;

public class ModuleDiagnosticTests
{
    [Test]
    [Arguments("[ElsaModule(\"orders\", 1)] public static class «Orders»;", "ELSAGEN005")]
    [Arguments("[ElsaModule(\"orders\", 1)] internal static partial class «Orders»;", "ELSAGEN005")]
    [Arguments("[ElsaModule(\"orders\", 1)] public static partial class Orders { public const int «Manifest» = 1; }", "ELSAGEN005")]
    [Arguments("[ElsaModule(«\" orders\"», 1)] public static partial class Orders;", "ELSAGEN006")]
    [Arguments("[ElsaModule(\"orders\", «0»)] public static partial class Orders;", "ELSAGEN006")]
    [Arguments("[ElsaModule(\"orders\", 1, «typeof(Box<>)»)] public static partial class Orders;", "ELSAGEN007")]
    [Arguments("[ElsaModule(\"orders\", 1, «typeof(Box<Local>)»)] public static partial class Orders;", "ELSAGEN007")]
    [Arguments("[ElsaModule(\"orders\", 1, typeof(string), «typeof(System.String)»)] public static partial class Orders;", "ELSAGEN008")]
    public async Task InvalidDeclarationReportsItsErrorAtTheOffendingToken(string markedDeclaration, string id)
    {
        const string path = "InvalidProducer.cs";
        var marked = $$"""
            using Elsa.CodeGeneration;
            namespace Fixtures;
            internal class Box<T>;
            file class Local;
            {{markedDeclaration}}
            """;
        var (source, expectedSpan) = Unmark(marked);
        var input = CompilerTest.CreateCompilation((path, source));
        var driver = CompilerTest.CreateDriver(new ElsaModuleGenerator()).Run(input, out _);
        var diagnostic = await Assert.That(driver.GetRunResult().Diagnostics).HasSingleItem();
        await AssertSourceError(diagnostic, id, path, expectedSpan);
        await Assert.That(driver.GetRunResult().GeneratedTrees).IsEmpty();
    }

    [Test]
    public async Task DuplicateModuleIdsIdentifyBothDeclarationsAndPreserveUnrelatedOutput()
    {
        var (first, firstSpan) = Unmark("""
            using Elsa.CodeGeneration;
            [ElsaModule(«"orders"», 1)] public static partial class Orders;
            """);
        var (second, secondSpan) = Unmark("""
            using Elsa.CodeGeneration;

            [ElsaModule(contractVersion: 2, moduleId: «"orders"»)]
            public static partial class Sales;
            """);
        var input = CompilerTest.CreateCompilation(
            ("First.cs", first),
            ("Second.cs", second),
            ("Shipping.cs", """[Elsa.CodeGeneration.ElsaModule("shipping", 1)] public static partial class Shipping;"""));
        var driver = CompilerTest.CreateDriver(new ElsaModuleGenerator()).Run(input, out var output);
        var diagnostics = driver.GetRunResult().Diagnostics.OrderBy(d => d.Location.SourceTree!.FilePath, StringComparer.Ordinal).ToArray();
        await Assert.That(diagnostics.Length).IsEqualTo(2);
        await AssertSourceError(diagnostics[0], "ELSAGEN009", "First.cs", firstSpan);
        await AssertSourceError(diagnostics[1], "ELSAGEN009", "Second.cs", secondSpan);
        await Assert.That(output.GetTypeByMetadataName("Orders")!.GetMembers("Manifest")).IsEmpty();
        await Assert.That(output.GetTypeByMetadataName("Sales")!.GetMembers("Manifest")).IsEmpty();
        await Assert.That(output.GetTypeByMetadataName("Shipping")!.GetMembers("Manifest")).HasSingleItem();
    }

    [Test]
    public async Task IncrementalEditMovesAnErrorAndCorrectingItGeneratesTheManifest()
    {
        const string path = "Orders.cs";
        var (original, span) = Unmark("""
            [Elsa.CodeGeneration.ElsaModule("orders", «0»)] public static partial class Orders;
            """);
        var input = CompilerTest.CreateCompilation((path, original));
        var driver = CompilerTest.CreateDriver(new ElsaModuleGenerator()).Run(input, out _);
        var first = await Assert.That(driver.GetRunResult().Diagnostics).HasSingleItem();
        await AssertSourceError(first, "ELSAGEN006", path, span);

        const string prefix = "// Header added during editing.\n";
        var tree = input.SyntaxTrees.Single();
        var movedTree = tree.WithChangedText(SourceText.From(prefix + original));
        input = input.ReplaceSyntaxTree(tree, movedTree);
        driver = driver.Run(input, out _);
        var moved = await Assert.That(driver.GetRunResult().Diagnostics).HasSingleItem();
        await AssertSourceError(moved, "ELSAGEN006", path, new TextSpan(span.Start + prefix.Length, span.Length));
        await Assert.That(moved.Location.SourceTree).IsSameReferenceAs(movedTree);

        var fixedTree = movedTree.WithChangedText(movedTree.GetText().WithChanges(new TextChange(moved.Location.SourceSpan, "1")));
        input = input.ReplaceSyntaxTree(movedTree, fixedTree);
        driver = driver.Run(input, out var output);
        await Assert.That(driver.GetRunResult().Diagnostics).IsEmpty();
        await Assert.That(output.GetTypeByMetadataName("Orders")!.GetMembers("Manifest")).HasSingleItem();
        await output.AssertEmits();
    }

    private static (string Source, TextSpan Span) Unmark(string marked)
    {
        var start = marked.IndexOf('«');
        var end = marked.IndexOf('»');
        return (marked.Remove(end, 1).Remove(start, 1), new TextSpan(start, end - start - 1));
    }

    private static async Task AssertSourceError(Diagnostic diagnostic, string id, string path, TextSpan span)
    {
        await Assert.That(diagnostic.Id).IsEqualTo(id);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.SourceTree?.FilePath).IsEqualTo(path);
        await Assert.That(diagnostic.Location.SourceSpan).IsEqualTo(span);
    }
}
