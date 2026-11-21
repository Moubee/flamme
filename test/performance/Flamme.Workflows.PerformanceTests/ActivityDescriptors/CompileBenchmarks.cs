using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Flamme.Workflows.PerformanceTests.ActivityDescriptors;

[Config(typeof(Config)), JsonExporterAttribute.Full, GcServer(true), GcConcurrent(true)]
public class CompileBenchmarks
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
    private static readonly CSharpCompilationOptions Options = new(OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release, concurrentBuild: false, deterministic: true,
        nullableContextOptions: NullableContextOptions.Enable);
    private MetadataReference[] _references = null!;

    private const string Producer = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Flamme.Workflows;
        using Flamme.Workflows.Attributes;
        using Flamme.Workflows.Models;

        namespace Shipping;

        [Activity("Shipping", "Label", 2)]
        public sealed class PrintLabel : CodeActivity
        {
            [Input(DefaultValue = 1)] public Input<int>? Copies { get; set; }
            [Output(Name = "TrackingCode")] public Output<string> Tracking { get; set; } = new();
        }

        public sealed class Dispatch : CodeActivity
        {
            [Port(Name = "AfterDispatch")] public IActivity? Next { get; set; }
            private ValueTask AfterDispatch(ActivityCompletedContext context) => default;
        }

        public sealed class Approval : CodeActivity
        {
            [Input] public Input<string>? Owner { get; set; }
            private ValueTask Resume(ActivityExecutionContext context) => default;
        }

        public sealed class Shipment : CodeActivity
        {
            public IList<IActivity> Steps { get; set; } = new List<IActivity>();
        }
        """;

    [GlobalSetup]
    public void Setup()
    {
        // Cache the same set of metadata references for every compile on this revision.
        string[] referenceNames =
        [
            "System.Private.CoreLib", "netstandard", "System.Runtime", "System.Collections",
            "System.Linq", "System.Threading.Tasks", "System.ComponentModel.Primitives",
            "Flamme.Workflows.Core", "Flamme.Common", "Flamme.Expressions", "Flamme.Mediator",
            "JetBrains.Annotations", "Microsoft.Extensions.DependencyInjection.Abstractions"
        ];
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var paths = trustedAssemblies.Split(Path.PathSeparator);
        var byName = paths.Distinct(StringComparer.Ordinal).ToDictionary(
            path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);
        _references = referenceNames.Select(name => MetadataReference.CreateFromFile(byName[name])).ToArray();

        var compilation = CreateCompilation();
        using var image = new MemoryStream();
        Emit(compilation, image);
        image.Position = 0;
        using var pe = new PEReader(image);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(reader.GetTypeDefinition).ToArray();
        var userTypes = types.Where(t => reader.GetString(t.Namespace) == "Shipping")
            .Select(t => reader.GetString(t.Name)).Order(StringComparer.Ordinal).ToArray();
        if (!userTypes.SequenceEqual(["Approval", "Dispatch", "PrintLabel", "Shipment"]))
            throw new InvalidOperationException("The compiler fixture must emit the same four public activity declarations.");

        var modules = types.Count(t => reader.GetString(t.Name) == "ActivityModule");
        var generated = compilation.SyntaxTrees.Where(t => t.FilePath.EndsWith("Flamme.ActivityModule.g.cs", StringComparison.Ordinal)).ToArray();
#if FLAMME_ACTIVITY_GENERATOR
        if (modules != 1 || generated.Length != 1)
            throw new InvalidOperationException("The producer has no generated Flamme inventory.");
#else
        if (modules != 0 || generated.Length != 0)
            throw new InvalidOperationException("Unexpected generated Flamme inventory in the baseline producer.");
#endif
        var sourceBytes = Encoding.UTF8.GetBytes(Producer);
        Console.WriteLine($"Compile fixture: activities={userTypes.Length}; inputBytes={sourceBytes.Length}; " +
            $"sourceSHA256={Convert.ToHexString(SHA256.HashData(sourceBytes))}; " +
            $"references={_references.Length}; roslyn={typeof(CSharpCompilation).Assembly.GetName().Version}; " +
            $"generatedFiles={generated.Length}; generatedChars={generated.Sum(t => t.Length)}; peBytes={image.Length}");
    }

    [Benchmark]
    public int CompileAndEmitProducer()
    {
        using var image = new MemoryStream();
        Emit(CreateCompilation(), image);
        return checked((int)image.Length);
    }

    private Compilation CreateCompilation()
    {
        var syntax = CSharpSyntaxTree.ParseText(Producer, ParseOptions, "Shipping.cs", Encoding.UTF8);
        var compilation = CSharpCompilation.Create("Shipping.Activities", [syntax], _references, Options);
#if FLAMME_ACTIVITY_GENERATOR
        var generator = new global::Flamme.ActivityGenerator.ActivityGenerator();
        var driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics));
        return generated;
#else
        return compilation;
#endif
    }

    private static void Emit(Compilation compilation, Stream image)
    {
        var result = compilation.Emit(image);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    }
}
