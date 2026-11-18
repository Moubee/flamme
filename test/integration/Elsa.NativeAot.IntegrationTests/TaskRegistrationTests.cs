using System.Text.Json;

namespace Elsa.NativeAot.IntegrationTests;

public class TaskRegistrationTests
{
    [Test]
    public async Task TaskConsumerRunsAnInternalTaskWithoutElsaDependencies()
    {
        var artifacts = BuildTest.CreateDirectory("task-runner");
        var project = Path.Combine(BuildTest.Root, "test/fixtures/TaskRunner.Consumer/TaskRunner.Consumer.csproj");
        var build = await BuildTest.Run(BuildTest.Root, Path.Combine(artifacts, "build.log"), "dotnet", "build", project,
            "-c", "Release", "-p:RestoreLockedMode=true", "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={artifacts}");
        build.EnsureSuccess();
        var output = Path.Combine(artifacts, "bin", "TaskRunner.Consumer", $"release_{BuildTest.Framework}");
        var run = await BuildTest.Run(output, Path.Combine(artifacts, "run.log"), "dotnet", "TaskRunner.Consumer.dll");
        run.EnsureSuccess();
        await Assert.That(run.Output.Trim()).IsEqualTo("cache warmed");

        // Examine the actual project/package graph of both runtime projects, and the deployed app closure.
        foreach (var path in new[]
        {
            Path.Combine(artifacts, "obj", "TaskRunner.Producer", "project.assets.json"),
            Path.Combine(artifacts, "obj", "TaskRunner.Consumer", "project.assets.json"),
            Path.Combine(output, "TaskRunner.Consumer.deps.json")
        })
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var dependencies = manifest.RootElement.GetProperty("libraries").EnumerateObject().Select(item => item.Name.Split('/')[0]).ToArray();
            await Assert.That(dependencies.Any(id => id.StartsWith("Elsa.", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
                || id is "RegistrationCompiler" or "TaskRunner.Generator")).IsFalse()
                .Because($"{path} resolved {string.Join(", ", dependencies)}");
        }
    }

    [Test]
    public async Task MixedHostLinksEachAdapterToItsOwnProducer()
    {
        var artifacts = BuildTest.CreateDirectory("mixed-adapters");
        var host = Directory.CreateDirectory(Path.Combine(artifacts, "host")).FullName;
        var fixtures = Path.Combine(BuildTest.Root, "test", "fixtures");
        var common = Path.Combine(BuildTest.Root, "src", "common");
        await File.WriteAllTextAsync(Path.Combine(host, "Mixed.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>{{BuildTest.Framework}}</TargetFramework>
                <ElsaExecutionMode>Generated</ElsaExecutionMode>
                <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{fixtures}}/TaskRunner.Producer/TaskRunner.Producer.csproj" />
                <ProjectReference Include="{{fixtures}}/TaskRunner.Generator/TaskRunner.Generator.csproj" ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
                <ProjectReference Include="{{fixtures}}/Elsa.NativeAot.Modules.Producer/Elsa.NativeAot.Modules.Producer.csproj" Aliases="feature" />
                <ProjectReference Include="{{common}}/Elsa.Features/Elsa.Features.csproj" />
              </ItemGroup>
              <Import Project="{{common}}/Elsa.CodeGeneration.Build/SourceTooling.targets" />
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(host, "Program.cs"), """
            extern alias feature;
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Attributes;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            using TaskRunner;
            using Producer = feature::Elsa.NativeAot.Modules.Producer;

            var state = new Producer.ProducerState();
            var module = new ServiceCollection().CreateModule();
            module.Properties[Producer.ProducerState.StatePropertyKey] = state;
            module.Configure<LocalRoot<Producer.ProducerState>>();
            module.Apply();
            Console.WriteLine($"{GeneratedTasks.Create().Single()().Run()}|{string.Join(",", state.Events)}");

            [DependsOn(typeof(Producer.FoundationFeature))]
            internal sealed class LocalRoot<T>(IModule module) : FeatureBase(module);
            """);

        var products = Path.Combine(artifacts, "build");
        var build = await BuildTest.Run(host, Path.Combine(artifacts, "build.log"), "dotnet",
            "build", "Mixed.csproj", "-c", "Release", "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={products}");
        build.EnsureSuccess();
        var runtime = Directory.GetFiles(Path.Combine(products, "bin", "Mixed"), "Mixed.dll", SearchOption.AllDirectories).Single();
        var run = await BuildTest.Run(host, Path.Combine(artifacts, "run.log"), "dotnet", runtime);
        run.EnsureSuccess();
        await Assert.That(run.Output.Trim()).IsEqualTo("cache warmed|foundation.configure,foundation.apply");

        var generated = Path.Combine(products, "obj", "Mixed");
        var taskHost = await File.ReadAllTextAsync(Directory.GetFiles(generated, "GeneratedTasks.g.cs", SearchOption.AllDirectories).Single());
        var featureHost = await File.ReadAllTextAsync(Directory.GetFiles(generated, "ElsaFeatureInterceptors.g.cs", SearchOption.AllDirectories).Single());
        await Assert.That(taskHost).Contains(".Register(tasks);");
        await Assert.That(featureHost).Contains("extern alias @feature;");
        await Assert.That(featureHost).Contains("@feature::");
    }
}
