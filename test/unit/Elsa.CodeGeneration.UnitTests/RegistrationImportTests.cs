using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using RegistrationCompiler;

namespace Elsa.CodeGeneration.UnitTests;

public class RegistrationImportTests
{
    [Test]
    public async Task AliasOnlyProducerUsesTheReferenceAliasExactlyOnce()
    {
        const string adapter = "TextWriter";
        const string assembly = "Class-library";
        var helper = RegistrationSource.EmitProducer(assembly, adapter, "global::System.Text.StringBuilder",
            ["""target.Append("ran");"""]);
        var producer = CompilerTest.CreateCompilation(("Producer.cs", helper)).WithAssemblyName(assembly);
        using var library = new MemoryStream();
        var emitted = producer.Emit(library);
        await Assert.That(emitted.Success).IsTrue().Because(string.Join("\n", emitted.Diagnostics));
        var reference = MetadataReference.CreateFromImage(library.ToArray()).WithAliases(["plugin", "class"]);
        var input = CompilerTest.CreateCompilation().AddReferences(reference);
        var imports = RegistrationSource.GetImports(input, adapter, "result", includeLocalProducer: false);
        await Assert.That(imports.ExternAliases.Single()).IsEqualTo("extern alias @class;");
        var source = $$"""
            {{imports.ExternAliases.Single()}}
            public static class Program
            {
                public static string Run()
                {
                    var result = new System.Text.StringBuilder();
                    {{imports.Calls.Single()}}
                    return result.ToString();
                }
            }
            """;
        using var executable = new MemoryStream();
        emitted = CompilerTest.CreateCompilation(("Host.cs", source)).AddReferences(reference).Emit(executable);
        await Assert.That(emitted.Success).IsTrue().Because(string.Join("\n", emitted.Diagnostics));

        var context = new AssemblyLoadContext(null, isCollectible: true);
        try
        {
            library.Position = executable.Position = 0;
            context.LoadFromStream(library);
            var host = context.LoadFromStream(executable);
            var run = host.GetType("Program", throwOnError: true)!.GetMethod("Run")!.CreateDelegate<Func<string>>();
            await Assert.That(run()).IsEqualTo("ran");
        }
        finally
        {
            context.Unload();
        }
    }
}
