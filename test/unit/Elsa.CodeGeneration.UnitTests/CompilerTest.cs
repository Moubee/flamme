using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Elsa.CodeGeneration.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Elsa.CodeGeneration.UnitTests;

internal static class CompilerTest
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
    private static readonly ImmutableArray<PortableExecutableReference> References = Directory
        .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "refs"), "System.*.dll")
        .Order(StringComparer.Ordinal)
        .Append(Path.Combine(AppContext.BaseDirectory, "Elsa.CodeGeneration.Abstractions.dll"))
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

    public static CSharpCompilation CreateCompilation(params (string Path, string Source)[] sources) =>
        CSharpCompilation.Create("Example", sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, ParseOptions, s.Path)),
            References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorDriver CreateDriver(params IIncrementalGenerator[] generators) => CreateDriver(false, generators);

    public static GeneratorDriver CreateDriver(bool isHost, params IIncrementalGenerator[] generators) =>
        CSharpGeneratorDriver.Create(generators.Select(generator => generator.AsSourceGenerator()),
            parseOptions: ParseOptions, optionsProvider: new ProducerBuildOptions(isHost));

    public static GeneratorDriver Run(this GeneratorDriver driver, CSharpCompilation input, out CSharpCompilation output)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var compilation, out _);
        output = (CSharpCompilation)compilation;
        return driver;
    }

    public static GeneratorDriverRunResult RunHostGenerators(CSharpCompilation input, out CSharpCompilation output)
    {
        var options = ((CSharpParseOptions)input.SyntaxTrees.First().Options)
            .WithFeatures(new Dictionary<string, string> { ["InterceptorsNamespaces"] = "Elsa.CodeGeneration.Interceptors" });
        input = input.RemoveAllSyntaxTrees().AddSyntaxTrees(input.SyntaxTrees.Select(tree => tree.WithRootAndOptions(tree.GetRoot(), options)));
        return CreateDriver(true, new ElsaFeatureGenerator(), new ElsaCompositionHostGenerator())
            .WithUpdatedParseOptions(options).Run(input, out output).GetRunResult();
    }

    public static async Task AssertEmits(this Compilation output)
    {
        using var assembly = new MemoryStream();
        var result = output.Emit(assembly);
        await Assert.That(result.Success).IsTrue().Because(string.Join(Environment.NewLine, result.Diagnostics));
    }

    private sealed class ProducerBuildOptions(bool isHost = false) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Empty = new Options(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_property.ElsaIsHost"] = isHost ? "true" : "false",
            ["build_property.ElsaGenerateMetadata"] = "true",
            ["build_property.ElsaEffectiveExecutionMode"] = "Generated",
            ["build_property.ElsaHostBootstrap"] = "Compiler",
            ["build_property.ElsaModuleExportSchemaVersion"] = "1"
        });

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
    }

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
