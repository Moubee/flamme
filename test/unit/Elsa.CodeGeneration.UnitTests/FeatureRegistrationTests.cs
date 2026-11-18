using System.Reflection;
using System.Runtime.Loader;
using Elsa.CodeGeneration.Generators;
using Elsa.Features.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.CodeGeneration.UnitTests;

public class FeatureRegistrationTests
{
    private const string Dependency = """
        using Elsa.Features.Abstractions;
        using Elsa.Features.Services;
        namespace Infrastructure;
        public sealed class DatabaseFeature(IModule module) : FeatureBase(module)
        {
            public override void Apply() => Module.Properties["steps"] += "database;";
        }
        """;

    private const string Producer = """
        using Elsa.Extensions;
        using Elsa.Features.Abstractions;
        using Elsa.Features.Attributes;
        using Elsa.Features.Contracts;
        using Elsa.Features.Implementations;
        using Elsa.Features.Services;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using System;
        using System.ComponentModel;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Infrastructure;
        namespace Application;

        [DependsOn(typeof(UnselectedFeature), Type = typeof(DatabaseFeature))]
        [DisplayName("Host \"plugin\""), Description("Inherited description")]
        internal abstract class PluginBase(IModule module) : FeatureBase(module);

        [DependencyOf(typeof(UnselectedFeature), Type = typeof(DatabaseFeature))]
        internal sealed class PluginFeature : PluginBase
        {
            public PluginFeature(IModule module) : base(module) => throw new Exception("wrong overload");
            public PluginFeature(Module module) : base(module) => module.Properties["steps"] = "module;";
            public int Priority { get; set; }
            public override void ConfigureHostedServices()
            {
                Module.ConfigureHostedService(typeof(Second), priority: Priority);
                ConfigureHostedService<First>(-1);
            }
            public override void Apply() => Module.Properties["steps"] += "plugin;";
        }

        internal sealed class CustomFeature(IModule module) : FeatureBase(module)
        {
            public required string Value { get; init; }
            public override void Apply() => Module.Properties["custom"] = Value;
        }
        internal sealed class UnselectedFeature : IFeature
        {
            public UnselectedFeature(IModule module) => throw new Exception("unselected");
            public IModule Module => throw new NotSupportedException();
            public void Configure() { }
            public void ConfigureHostedServices() { }
            public void Apply() { }
        }
        internal sealed class GenericFeature<T>(IModule module) : FeatureBase(module);
        [DependencyOf(typeof(UnselectedFeature))]
        internal sealed class ExcludedFeature(IModule module) : FeatureBase(module)
        {
            public override void Apply() => throw new Exception("conditional feature was admitted");
        }
        internal class First : IHostedService
        {
            public Task StartAsync(CancellationToken token) => Task.CompletedTask;
            public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        }
        internal sealed class Second : First;

        public static class Scenario
        {
            public static string Run(ExplicitFeatureCatalog catalog)
            {
                if (!catalog.FeatureTypes.Contains(typeof(UnselectedFeature)))
                    throw new Exception("missing available direct IFeature implementation");
                var services = new ServiceCollection();
                var module = services.CreateModule(catalog);
                module.Configure<PluginFeature>(p => p.Priority = 42);
                module.Configure(m => new CustomFeature(m) { Value = "caller" });
                module.Use<GenericFeature<int>>();
                module.Configure<ExcludedFeature>();
                module.Apply();
                module.Apply();
                var registry = (IInstalledFeatureRegistry)services.Last(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance!;
                var info = registry.Find("Elsa.Plugin")!;
                var hosted = string.Join(",", services.Where(s => s.ServiceType == typeof(IHostedService))
                    .Select(s => s.ImplementationType!.Name + "/" + s.Lifetime));
                return string.Join("|", module.Properties["steps"], module.Properties["custom"], info.DisplayName,
                    info.Description, registry.Find("Elsa.Generic`1")!.Name, hosted);
            }
        }
        """;

    [Test]
    public async Task ProducerBindingsExecuteAcrossAssemblies()
    {
        var dependencyInput = Create("Infrastructure", ("Dependency.cs", Dependency));
        var dependency = Compile(dependencyInput);
        var dependencyImage = Emit(dependency);
        var input = Create("Application", ("Plugin.cs", Producer)).AddReferences(MetadataReference.CreateFromImage(dependencyImage));
        var producer = Compile(input);
        var context = new AssemblyLoadContext("feature-bindings", isCollectible: true);
        try
        {
            var infrastructure = context.LoadFromStream(new MemoryStream(dependencyImage));
            var app = context.LoadFromStream(new MemoryStream(Emit(producer)));
            var builder = new ExplicitFeatureCatalogBuilder();
            Register(infrastructure, builder);
            Register(app, builder);
            var run = app.GetType("Application.Scenario", throwOnError: true)!.GetMethod("Run")!;
            var actual = run.CreateDelegate<Func<ExplicitFeatureCatalog, string>>()(builder.BuildAvailable());
            await Assert.That(actual).IsEqualTo("module;database;plugin;database;plugin;|caller|Host \"plugin\"|Inherited description|Generic`1|First/Singleton,Second/Singleton");
        }
        finally
        {
            context.Unload();
        }
    }

    [Test]
    public async Task EditsAndRemovalsProduceTheSameBindingsAsACleanCompilation()
    {
        const string source = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using System.ComponentModel;
            [Description("old")]
            internal sealed class SampleFeature(IModule module) : FeatureBase(module);
            """;
        var input = Create("Producer", ("Sample.cs", source), ("Other.cs", Dependency));
        var driver = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(input, out _);
        var old = input.SyntaxTrees.First();
        var edited = input.ReplaceSyntaxTree(old, old.WithChangedText(SourceText.From(source.Replace("old", "new"))));
        driver = driver.Run(edited, out var editedOutput);
        await CompareWithClean(edited, editedOutput, driver);
        var removed = edited.RemoveSyntaxTrees(edited.SyntaxTrees.First());
        driver = driver.Run(removed, out var removedOutput);
        await CompareWithClean(removed, removedOutput, driver);
        var empty = removed.RemoveAllSyntaxTrees();
        driver = driver.Run(empty, out _);
        await Assert.That(driver.GetRunResult().GeneratedTrees).IsEmpty();
    }

    [Test]
    public async Task InaccessibleHostRequestPointsToTheCall()
    {
        const string call = "module.Configure<HiddenFeature>()";
        const string source = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public class Host
            {
                private sealed class HiddenFeature(IModule module) : FeatureBase(module);
                public static void Start(IServiceCollection services, ExplicitFeatureCatalog explicitCatalog)
                {
                    services.CreateModule(explicitCatalog).Configure<HiddenFeature>();
                    var module = services.CreateModule();
                    module.Configure<HiddenFeature>();
                }
            }
            """;
        var input = Create("Host", ("Request.cs", source));
        var driver = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(input, out var output);
        var diagnostic = driver.GetRunResult().Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN010");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.SourceTree!.FilePath).IsEqualTo("Request.cs");
        await Assert.That(diagnostic.Location.SourceSpan).IsEqualTo(new TextSpan(source.IndexOf(call, StringComparison.Ordinal), call.Length));
        await Assert.That(diagnostic.GetMessage()).Contains("Host.Start");
        await Assert.That(diagnostic.GetMessage()).Contains("Host.HiddenFeature");
        await output.AssertEmits();
    }

    [Test]
    [Arguments("[CustomDependency]", "derived attribute")]
    [Arguments("[DependsOn(typeof(HiddenFeature))]", "accessible, closed feature type")]
    public async Task UnsupportedMetadataIdentifiesTheRequestedFeature(string attribute, string reason)
    {
        const string call = "module.Configure<PluginFeature>()";
        var source = $$"""
            using Elsa.Features.Abstractions;
            using Elsa.Features.Attributes;
            using Elsa.Features.Services;
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                private sealed class HiddenFeature(IModule module) : FeatureBase(module);
                private sealed class CustomDependencyAttribute() : DependsOnAttribute(typeof(HiddenFeature));
                {{attribute}}
                public sealed class PluginFeature(IModule module) : FeatureBase(module);
                public static void Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    module.Configure<PluginFeature>();
                }
            }
            """;
        var input = Create("Host", ("Request.cs", source));
        var diagnostic = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(input, out var output)
            .GetRunResult().Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN010");
        await Assert.That(diagnostic.Location.SourceSpan).IsEqualTo(new TextSpan(source.IndexOf(call, StringComparison.Ordinal), call.Length));
        await Assert.That(diagnostic.GetMessage()).Contains(reason);
        await Assert.That(diagnostic.GetMessage()).Contains("PluginFeature");
        await output.AssertEmits();
    }

    [Test]
    public async Task RebuildingAReferencedFeatureWithCompilerToolingClearsItsRequestDiagnostic()
    {
        const string call = "module.Configure<Infrastructure.DatabaseFeature>()";
        const string source = """
            using Elsa.Features.Services;
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    module.Configure<Infrastructure.DatabaseFeature>();
                }
            }
            """;
        var library = Create("Infrastructure", ("Dependency.cs", Dependency));
        var plain = MetadataReference.CreateFromImage(Emit(library));
        var host = Create("Host", ("Host.cs", source)).AddReferences(plain);
        var driver = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(host, out _);
        var missing = driver.GetRunResult().Diagnostics.Single();
        await Assert.That(missing.Id).IsEqualTo("ELSAGEN010");
        await Assert.That(missing.Location.SourceTree!.FilePath).IsEqualTo("Host.cs");
        await Assert.That(missing.Location.SourceSpan).IsEqualTo(new TextSpan(source.IndexOf(call, StringComparison.Ordinal), call.Length));
        await Assert.That(missing.GetMessage()).Contains("Host.Start");
        await Assert.That(missing.GetMessage()).Contains("Infrastructure.DatabaseFeature");

        var boundLibrary = MetadataReference.CreateFromImage(Emit(Compile(library)));
        var repaired = host.ReplaceReference(plain, boundLibrary);
        driver = driver.Run(repaired, out var output);
        var clean = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(repaired, out _);
        await Assert.That(driver.GetRunResult().Diagnostics).IsEmpty();
        await Assert.That(clean.GetRunResult().Diagnostics).IsEmpty();
        await Assert.That(driver.GetRunResult().GeneratedTrees.Single().ToString()).IsEqualTo(clean.GetRunResult().GeneratedTrees.Single().ToString());
        await output.AssertEmits();
    }

    private static CSharpCompilation Create(string assembly, params (string, string)[] sources) =>
        CompilerTest.CreateCompilation(sources).WithAssemblyName(assembly).AddReferences(
            MetadataReference.CreateFromFile(typeof(IFeature).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IHostedService).Assembly.Location));

    private static CSharpCompilation Compile(CSharpCompilation input)
    {
        var driver = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(input, out var output);
        if (!driver.GetRunResult().Diagnostics.IsEmpty)
            throw new InvalidOperationException(string.Join(Environment.NewLine, driver.GetRunResult().Diagnostics));
        return output;
    }

    private static void Register(Assembly assembly, ExplicitFeatureCatalogBuilder builder)
    {
        var marker = assembly.GetCustomAttributesData().Single(a => a.AttributeType.Name == "ProducerAttribute");
        var bindings = (Type)marker.ConstructorArguments[1].Value!;
        bindings.GetMethod("Register")!.CreateDelegate<Action<ExplicitFeatureCatalogBuilder>>()(builder);
    }

    private static byte[] Emit(CSharpCompilation compilation)
    {
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return image.ToArray();
    }

    private static async Task CompareWithClean(CSharpCompilation input, CSharpCompilation output, GeneratorDriver driver)
    {
        var clean = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(input, out var cleanOutput);
        var expected = clean.GetRunResult().GeneratedTrees.Single().ToString();
        await Assert.That(driver.GetRunResult().GeneratedTrees.Single().ToString()).IsEqualTo(expected);
        await Assert.That(driver.GetRunResult().Diagnostics).IsEmpty();
        await output.AssertEmits();
        await cleanOutput.AssertEmits();
    }
}
