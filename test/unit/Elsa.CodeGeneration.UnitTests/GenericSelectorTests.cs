using Elsa.CodeGeneration.Generators;
using Elsa.Features.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.CodeGeneration.UnitTests;

public class GenericSelectorTests
{
    private const string Helpers = """
        using Elsa.Extensions;
        using Elsa.Features.Services;
        public static class Helpers
        {
            public static void Select<T>(IModule module) where T : class, IFeature => module.Use<T>();
            public static void SelectWithFactory<T>(IModule module, System.Func<IModule, T> factory)
                where T : class, IFeature => module.Configure(factory);
            public static void Swap<TFirst,TSecond>(this IModule module)
                where TFirst : class, IFeature where TSecond : class, IFeature
            {
                Select<TSecond>(module);
                Select<TFirst>(module);
            }
        }
        """;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClosedCallsHaveTheirOwnSelectionsAndForwardTypeArgumentsInOrder(bool referenceAssembly)
    {
        const string host = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public sealed class Other<T>(IModule module) : FeatureBase(module);
            public sealed class Custom(IModule module, int value) : FeatureBase(module);
            public static class Host
            {
                public static void Start(IServiceCollection services)
                {
                    var first = services.CreateModule();
                    Helpers.Select<Leaf>(first);
                    var second = services.CreateModule();
                    second.Swap<Leaf, Other<int>>();
                    Helpers.SelectWithFactory(second, m => new Custom(m, 42));
                }
            }
            """;

        var input = Create("Host", host);
        if (referenceAssembly)
        {
            var package = Create("External", Helpers);
            CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(package, out var compiled);
            using var binary = new MemoryStream();
            var emitted = compiled.Emit(binary, options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
            await Assert.That(emitted.Success).IsTrue().Because(string.Join("\n", emitted.Diagnostics));
            input = input.AddReferences(MetadataReference.CreateFromImage(binary.ToArray()));
        }
        else input = input.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Helpers, (CSharpParseOptions)input.SyntaxTrees.Single().Options, "Helpers.cs"));

        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
        await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
        var generated = result.GeneratedTrees.Single().ToString();
        var plans = generated.Split(" : FixedFeatureModule");
        await Assert.That(plans.Length).IsEqualTo(3);
        await Assert.That(plans[1]).Contains(".DeclareLeaf_");
        await Assert.That(plans[1].Contains(".DeclareOther_", StringComparison.Ordinal)).IsFalse();
        await Assert.That(plans[2].IndexOf(".DeclareOther_", StringComparison.Ordinal)).IsGreaterThanOrEqualTo(0);
        await Assert.That(plans[2].IndexOf(".DeclareLeaf_", StringComparison.Ordinal)).IsGreaterThan(plans[2].IndexOf(".DeclareOther_", StringComparison.Ordinal));
        await Assert.That(plans[2]).Contains(".DeclareCustom_");
    }

    [Test]
    public async Task SourceLocalFunctionsMethodGroupsAndIndependentInputsBindAtTheCall()
    {
        const string host = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Root(IModule module) : FeatureBase(module);
            public sealed class Left(IModule module) : FeatureBase(module);
            public sealed class Right(IModule module) : FeatureBase(module);
            public sealed class Local(IModule module) : FeatureBase(module);
            public sealed class Group<T>(IModule module) : FeatureBase(module);
            public static class Host
            {
                static void Both<TLeft, TRight>(IModule left, IModule right)
                    where TLeft : class, IFeature where TRight : class, IFeature
                {
                    left.Use<TLeft>();
                    right.Use<TRight>();
                }
                static void Setup<T>(Root root) where T : class, IFeature => root.Module.Use<T>();
                public static void Start(IServiceCollection services)
                {
                    var first = services.CreateModule();
                    var second = services.CreateModule();
                    Both<Left, Right>(first, second);
                    Configure<Local>(first);
                    second.Configure<Root>(Setup<Group<int>>);
                    static void Configure<T>(IModule module) where T : class, IFeature => module.Use<T>();
                }
            }
            """;
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(Create("Host", host), out _).GetRunResult();
        await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
        var plans = result.GeneratedTrees.Single().ToString().Split(" : FixedFeatureModule");
        await Assert.That(plans.Length).IsEqualTo(3);
        await Assert.That(plans[1]).Contains(".DeclareLeft_");
        await Assert.That(plans[1]).Contains(".DeclareLocal_");
        await Assert.That(plans[1].Contains(".DeclareRight_", StringComparison.Ordinal)).IsFalse();
        await Assert.That(plans[2]).Contains(".DeclareRight_");
        await Assert.That(plans[2]).Contains(".DeclareGroup_");
        await Assert.That(plans[2].Contains(".DeclareLocal_", StringComparison.Ordinal)).IsFalse();
        await Assert.That(plans[2].Contains(".DeclareLeft_", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task OpenConstructedFeatureInsideSelectorProducesADiagnostic()
    {
        const string host = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Wrapper<T>(IModule module) : FeatureBase(module);
            public static class Host
            {
                static void SelectWrapper<T>(IModule module) => module.Use<Wrapper<T>>();
                public static void Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    SelectWrapper<int>(module);
                }
            }
            """;
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(Create("Host", host), out _).GetRunResult();
        var diagnostic = result.Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("not statically known");
    }

    private static CSharpCompilation Create(string name, string source) => CompilerTest.CreateCompilation((name + ".cs", source))
        .WithAssemblyName(name).AddReferences(MetadataReference.CreateFromFile(typeof(IFeature).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IHostedService).Assembly.Location));
}
