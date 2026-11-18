using Elsa.CodeGeneration.Generators;
using Elsa.Features.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.CodeGeneration.UnitTests;

public class CompositionRootDiagnosticTests
{
    [Test]
    public async Task DirectConstructionAndRootMethodGroupsIdentifyTheExactSourceTokens()
    {
        const string source = """
            using System;
            using Elsa.Extensions;
            using Elsa.Features.Implementations;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;

            class App
            {
                void Configure(IServiceCollection services, ExplicitFeatureCatalog catalog)
                {
                    _ = new Module(services);
                    Module implicitModule = new(services);
                    Func<IModule> create = services.CreateModule;
                    _ = new Module(services, catalog);
                    _ = services.CreateModule(catalog);
                    _ = FeatureModuleCatalogBuilder.CreateModule(services, [], default);
                    _ = new Module(services, catalog, Elsa.Features.Models.CompositionPolicy.ValidatedV1);
                    Func<IModule> supported = () => services.CreateModule();
                    _ = new Other().CreateModule();
                }
            }
            class Other { public int CreateModule() => 0; }
            namespace Elsa.CodeGeneration
            {
                internal static partial class ElsaHost
                {
                    private static IModule CreateDynamicModule(IServiceCollection services) => new Module(services);
                }
            }
            """;
        var input = CompilerTest.CreateCompilation(("App.cs", source)).AddReferences(
            MetadataReference.CreateFromFile(typeof(IModule).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        var driver = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _);
        var result = driver.GetRunResult();
        var diagnostics = result.Diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToArray();
        await Assert.That(diagnostics.Length).IsEqualTo(8);
        foreach (var (index, id, token, start) in new[]
                 {
                     (0, "ELSAGEN005", "Module", source.IndexOf("Module(services)", StringComparison.Ordinal)),
                     (1, "ELSAGEN005", "new", source.IndexOf("new(services)", StringComparison.Ordinal)),
                     (2, "ELSAGEN006", "CreateModule", source.IndexOf("CreateModule;", StringComparison.Ordinal)),
                     (3, "ELSAGEN005", "Module", source.IndexOf("Module(services, catalog)", StringComparison.Ordinal)),
                     (4, "ELSAGEN014", "CreateModule", source.IndexOf("CreateModule(catalog)", StringComparison.Ordinal)),
                     (5, "ELSAGEN014", "CreateModule", source.IndexOf("CreateModule(services, []", StringComparison.Ordinal)),
                     (6, "ELSAGEN005", "Module", source.IndexOf("Module(services, catalog,", StringComparison.Ordinal)),
                     (7, "ELSAGEN005", "Module", source.LastIndexOf("Module(services)", StringComparison.Ordinal))
                 })
        {
            await Assert.That(diagnostics[index].Id).IsEqualTo(id);
            await Assert.That(diagnostics[index].Location.SourceTree!.FilePath).IsEqualTo("App.cs");
            await Assert.That(diagnostics[index].Location.SourceSpan).IsEqualTo(new TextSpan(start, token.Length));
        }
        var intercepted = result.Results.Single().GeneratedSources.Single().SourceText.ToString();
        await Assert.That(intercepted.Split("[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(").Length - 1)
            .IsEqualTo(1);

        var library = CompilerTest.CreateDriver(new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
        await Assert.That(library.Diagnostics.Length).IsEqualTo(0);
        await Assert.That(library.GeneratedTrees.Length).IsEqualTo(0);
    }
}
