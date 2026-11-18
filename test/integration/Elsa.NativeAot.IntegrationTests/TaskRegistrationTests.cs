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
}
