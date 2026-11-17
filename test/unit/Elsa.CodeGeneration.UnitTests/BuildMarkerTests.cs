using Elsa.CodeGeneration.Generators;
using Microsoft.CodeAnalysis;

namespace Elsa.CodeGeneration.UnitTests;

public class BuildMarkerTests
{
    [Test]
    public async Task GeneratedLibraryEmitsItsProviderMarker()
    {
        var library = CompilerTest.CreateCompilation(("Consumer.cs", """
            public class Consumer
            {
                public const string Mode = Elsa.CodeGeneration.ElsaGenerationBuildMarker.EffectiveExecutionMode;
                public const bool IsHost = Elsa.CodeGeneration.ElsaGenerationBuildMarker.IsHost;
            }
            """));
        var driver = CompilerTest.CreateDriver(new ElsaHostGenerator()).Run(library, out var output);

        var result = driver.GetRunResult();
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.Results.Single().GeneratedSources.Single().HintName).IsEqualTo("ElsaGenerationBuildMarker.g.cs");
        await output.AssertEmits();

        var consumer = output.GetTypeByMetadataName("Consumer")!;
        await Assert.That(((IFieldSymbol)consumer.GetMembers("Mode").Single()).ConstantValue).IsEqualTo("Generated");
        var isHost = (IFieldSymbol)consumer.GetMembers("IsHost").Single();
        await Assert.That((bool)isHost.ConstantValue!).IsFalse();
    }
}
