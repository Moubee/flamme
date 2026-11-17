using Elsa.CodeGeneration.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.CodeGeneration.UnitTests;

public class ModuleRebuildTests
{
    private const string Orders = """
        namespace Sample;
        [Elsa.CodeGeneration.ElsaModule("orders", 1, typeof(OldOrder))]
        public static partial class Orders;
        public sealed class OldOrder;
        """;

    private const string Second = """
        namespace Sample;
        [Elsa.CodeGeneration.ElsaModule("another-module", 1, typeof(int))]
        public static partial class ORDERS;
        """;

    [Test]
    public async Task RebuildMatchesCleanGenerationAfterEditingAndRemovingDeclarations()
    {
        var input = CompilerTest.CreateCompilation(("Orders.cs", Orders), ("Second.cs", Second));
        var driver = CompilerTest.CreateDriver(new ElsaModuleGenerator()).Run(input, out var initial);
        await initial.AssertEmits();
        var originalSources = GeneratedSources(driver);
        await Assert.That(originalSources.Length).IsEqualTo(2);
        await Assert.That(initial.GetTypeByMetadataName("Sample.Orders")!.GetMembers("Manifest")).HasSingleItem();
        await Assert.That(initial.GetTypeByMetadataName("Sample.ORDERS")!.GetMembers("Manifest")).HasSingleItem();

        driver = driver.Run(input, out var repeated);
        await repeated.AssertEmits();
        await Assert.That(GeneratedSources(driver).SequenceEqual(originalSources)).IsTrue();

        var editedOrders = Orders.Replace("OldOrder", "NewOrder", StringComparison.Ordinal);
        var oldTree = input.SyntaxTrees.Single(tree => tree.FilePath == "Orders.cs");
        input = input.ReplaceSyntaxTree(oldTree, oldTree.WithChangedText(SourceText.From(editedOrders)));
        driver = driver.Run(input, out var edited);
        await edited.AssertEmits();
        var cleanInput = CompilerTest.CreateCompilation(("Second.cs", Second), ("Orders.cs", editedOrders));
        var cleanDriver = CompilerTest.CreateDriver(new ElsaModuleGenerator()).Run(cleanInput, out var clean);
        await clean.AssertEmits();
        await Assert.That(GeneratedSources(driver).SequenceEqual(GeneratedSources(cleanDriver))).IsTrue();

        input = input.RemoveSyntaxTrees(input.SyntaxTrees.Single(tree => tree.FilePath == "Second.cs"));
        driver = driver.Run(input, out var remaining);
        await remaining.AssertEmits();
        var manifest = await Assert.That(driver.GetRunResult().GeneratedTrees).HasSingleItem();
        var typeOf = await Assert.That(manifest.GetRoot().DescendantNodes().OfType<TypeOfExpressionSyntax>()).HasSingleItem();
        var declared = remaining.GetSemanticModel(manifest).GetTypeInfo(typeOf.Type).Type;
        await Assert.That(declared?.ToDisplayString()).IsEqualTo("Sample.NewOrder");
        await Assert.That(remaining.GetTypeByMetadataName("Sample.ORDERS")).IsNull();
        await Assert.That(remaining.GetTypeByMetadataName("Sample.Orders")!.GetMembers("Manifest")).HasSingleItem();
    }

    private static (string HintName, string Text)[] GeneratedSources(GeneratorDriver driver) =>
        driver.GetRunResult().Results.Single().GeneratedSources
            .Select(source => (source.HintName, source.SourceText.ToString()))
            .OrderBy(source => source.HintName, StringComparer.Ordinal)
            .ToArray();
}
