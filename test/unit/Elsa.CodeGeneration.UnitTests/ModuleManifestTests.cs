using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using Elsa.CodeGeneration.Generators;
using Microsoft.CodeAnalysis;

namespace Elsa.CodeGeneration.UnitTests;

public class ModuleManifestTests
{
    [Test]
    public async Task PartialProducerExposesOnePublicManifestForItsExplicitClosedTypes()
    {
        var input = CompilerTest.CreateCompilation(
            ("Orders.cs", """
                using Elsa.CodeGeneration;

                namespace Fixtures;

                [ElsaModule(contractVersion: 2, moduleId: "orders", declaredTypes: new System.Type[] {
                    typeof(Orders.Local), typeof(Envelope<int>), typeof(ApprovedOrder)
                })]
                public static partial class Orders
                {
                    private sealed class Local;
                    public static readonly ModuleTypeManifest Selected = Manifest;

                    public static int Main()
                    {
                        var manifest = Manifest;
                        if (!ReferenceEquals(manifest, Selected))
                            throw new System.InvalidOperationException("The producer's own initializer did not receive the manifest.");
                        if (manifest.Descriptor.ModuleId != "orders" || manifest.Descriptor.ContractVersion != 2)
                            throw new System.InvalidOperationException("Incorrect module identity or contract version.");
                        System.Type[] expected = [typeof(ApprovedOrder), typeof(Envelope<int>), typeof(Local)];
                        return System.Linq.Enumerable.SequenceEqual(manifest.DeclaredTypes, expected) ? 0 : 1;
                    }
                }
                """),
            ("OrderTypes.cs", """
                namespace Fixtures;

                internal abstract class Order;
                internal sealed class ApprovedOrder : Order;
                internal partial class Envelope<T>;
                internal partial class Envelope<T>;
                public static partial class Orders;
                """));

        input = input.WithOptions(input.Options.WithOutputKind(OutputKind.ConsoleApplication));
        var driver = CompilerTest.CreateDriver(new ElsaModuleGenerator()).Run(input, out var output);
        await Assert.That(driver.GetRunResult().Diagnostics).IsEmpty();
        var shell = output.GetTypeByMetadataName("Fixtures.Orders")!;
        var property = (IPropertySymbol)shell.GetMembers("Manifest").Single();
        await Assert.That(property.DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(property.IsStatic).IsTrue();
        await Assert.That(property.Type.ToDisplayString()).IsEqualTo("Elsa.CodeGeneration.ModuleTypeManifest");

        await AssertProducerReturnsZero(output);
    }

    [Test]
    public async Task KeywordScopesAndCSharpAliasesPreserveTheRequestedClrTypes()
    {
        var input = CompilerTest.CreateCompilation(("KeywordProducer.cs", """
            using Elsa.CodeGeneration;
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace @event.@class;

            [ElsaModule("keywords", 1, typeof(UIntPtr), typeof(List<dynamic>), typeof((int Count, string Name)), typeof(IntPtr))]
            public static partial class @switch
            {
                private class nint;
                private class nuint;
                public static int Main()
                {
                    Type[] expected = [typeof(List<object>), typeof(IntPtr), typeof(UIntPtr), typeof(ValueTuple<int, string>)];
                    return Manifest.DeclaredTypes.SequenceEqual(expected) ? 0 : 1;
                }
            }
            """));
        input = input.WithOptions(input.Options.WithOutputKind(OutputKind.ConsoleApplication));
        var driver = CompilerTest.CreateDriver(new ElsaModuleGenerator()).Run(input, out var output);
        await Assert.That(driver.GetRunResult().Diagnostics).IsEmpty();
        await AssertProducerReturnsZero(output);
    }

    private static async Task AssertProducerReturnsZero(Compilation output)
    {
        using var image = new MemoryStream();
        var emit = output.Emit(image);
        await Assert.That(emit.Success).IsTrue().Because(string.Join(Environment.NewLine, emit.Diagnostics));
        image.Position = 0;
        await Assert.That(RunProducer(image)).IsEqualTo(0).Because("coverage must contain exactly the declared CLR types in ordinal order");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This managed compiler test executes the entry point of its own freshly emitted assembly.")]
    private static int RunProducer(Stream image)
    {
        var context = new AssemblyLoadContext(nameof(ModuleManifestTests), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            return assembly.EntryPoint!.CreateDelegate<Func<int>>()();
        }
        finally
        {
            context.Unload();
        }
    }
}
