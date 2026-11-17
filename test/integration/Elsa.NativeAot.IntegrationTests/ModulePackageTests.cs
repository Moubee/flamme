using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

[Explicit]
[Category("NativeAotAcceptance")]
[NotInParallel("NativeAotBuild")]
public class ModulePackageTests
{
    private const string Producer = "Elsa.NativeAot.Modules.Producer";
    private const string ConsumerProject = "Consumer.csproj";

    [Test]
    [Arguments("managed")]
    [Arguments("native")]
    public Task PackedModuleRuns(string mode) => PublishAndRun(mode, disableRuntimeAsync: false);

#if NET11_0_OR_GREATER
    [Test]
    [Arguments("managed")]
    [Arguments("native")]
    public Task PackedModuleRunsWithoutRuntimeAsync(string mode) => PublishAndRun(mode, disableRuntimeAsync: true);
#endif

    [Test]
    [Arguments(false)]
#if NET11_0_OR_GREATER
    [Arguments(true)]
#endif
    public async Task ProducerBuildUsesExpectedCompilerSettings(bool disableRuntimeAsync)
    {
        var feed = await PackageFeed.Get(disableRuntimeAsync);
        string[] frameworks = BuildTest.Framework == "net11.0" ? ["net10.0", "net11.0"] : [BuildTest.Framework];
        foreach (var project in new[] { "src/common/Elsa.CodeGeneration.Abstractions", "src/common/Elsa.Features", $"test/fixtures/{Producer}" })
        {
            var name = Path.GetFileName(project);
            foreach (var framework in frameworks)
            {
                var properties = await ReadBuildProperties(BuildTest.Root, Path.Combine(project, name + ".csproj"),
                    Path.Combine(feed.Artifacts, $"{name}-{framework}"), framework, feed.PackProperties);
                await VerifyCompilerSettings(properties, framework, disableRuntimeAsync);
                await Assert.That(properties.GetProperty("IsPackable").GetString()).IsEqualTo("true");
                await Assert.That(properties.GetProperty("PackageId").GetString()).IsEqualTo(name);
                await Assert.That(properties.GetProperty("PackageVersion").GetString()).IsEqualTo(feed.Version);
            }
        }
    }

    [Test]
    public async Task EmptyFeedCannotRestoreProducer()
    {
        var consumer = Directory.CreateTempSubdirectory("flamme-missing-module-").FullName;
        var evidence = BuildTest.CreateDirectory("missing-module");
        try
        {
            var emptyFeed = Directory.CreateDirectory(Path.Combine(consumer, "empty & local")).FullName;
            const string version = "0.0.0-missing";
            File.Copy(Path.Combine(BuildTest.Root, "global.json"), Path.Combine(consumer, "global.json"));
            File.WriteAllText(Path.Combine(consumer, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Combine(consumer, "Directory.Build.targets"), "<Project />");
            new XElement("Project", new XElement("PropertyGroup", new XElement("ManagePackageVersionsCentrally", "false")))
                .Save(Path.Combine(consumer, "Directory.Packages.props"));
            new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", BuildTest.Framework)),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", Producer), new XAttribute("Version", $"[{version}]"))))
                .Save(Path.Combine(consumer, ConsumerProject));
            new XElement("configuration", new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "empty-fixture"), new XAttribute("value", emptyFeed))))
                .Save(Path.Combine(consumer, "NuGet.Config"));

            var failure = await BuildTest.Run(consumer, Path.Combine(evidence, "restore.log"), "dotnet", "restore", ConsumerProject,
                "--configfile", "NuGet.Config", "--packages", Path.Combine(consumer, "packages"), "-p:RestoreFallbackFolders=", "-p:NuGetAudit=false");
            await Assert.That(failure.ExitCode).IsNotEqualTo(0);
            await Assert.That(failure.Output).Matches($@"error NU110[12]:[^\r\n]*{Regex.Escape(Producer)}");
        }
        finally
        {
            Directory.Delete(consumer, recursive: true);
        }
    }

    private static async Task PublishAndRun(string mode, bool disableRuntimeAsync)
    {
        var feed = await PackageFeed.Get(disableRuntimeAsync);
        var name = $"module-{mode}" + (disableRuntimeAsync ? "-async-disabled" : "");
        var consumer = feed.PrepareConsumer(name);
        var evidence = BuildTest.CreateDirectory(name);
        var build = Path.Combine(evidence, "build");
        var templates = Path.Combine(BuildTest.Root, "test", "fixtures", "nativeaot", "module-consumer");
        File.Copy(Path.Combine(templates, "Consumer.csproj.in"), Path.Combine(consumer, ConsumerProject));
        foreach (var file in new[] { "Program.cs", "ModuleConsumerScenarios.cs" })
            File.Copy(Path.Combine(templates, file), Path.Combine(consumer, file));
        var snapshot = Directory.CreateDirectory(Path.Combine(evidence, "source")).FullName;
        foreach (var file in Directory.EnumerateFiles(consumer))
            File.Copy(file, Path.Combine(snapshot, Path.GetFileName(file)));

        string[] options = ["-p:Configuration=Release", $"-p:NativeAotMode={mode}", $"-p:RuntimeIdentifier={BuildTest.Rid}",
            "-p:NoWarn=CS1591", "-p:EnableTrimAnalyzer=true", "-p:EnableAotAnalyzer=true", "-p:TrimmerSingleWarn=false",
            "-p:SuppressTrimAnalysisWarnings=false", "-p:ILLinkTreatWarningsAsErrors=true", "-p:IlcTreatWarningsAsErrors=true"];
        if (disableRuntimeAsync)
            options = [.. options, "-p:UseRuntimeAsync=false"];

        Task<CommandResult> Run(string log, string executable, params string[] args) =>
            BuildTest.Run(consumer, Path.Combine(evidence, log + ".log"), executable, args);

        (await Run("generate-lock", "dotnet", ["restore", ConsumerProject, "--force-evaluate", "--artifacts-path", build, .. options])).EnsureSuccess();
        var lockFile = Path.Combine(consumer, "locks", mode, BuildTest.Rid, "packages.lock.json");
        PackageFeed.VerifyLock(lockFile);
        File.Copy(lockFile, Path.Combine(evidence, "packages.lock.json"));
        (await Run("locked-restore", "dotnet", ["restore", ConsumerProject, "--locked-mode", "--artifacts-path", build, .. options])).EnsureSuccess();
        feed.VerifyAssets(Path.Combine(build, "obj", "Consumer", "project.assets.json"), false,
            "Elsa.CodeGeneration", "Elsa.CodeGeneration.Analyzers", "Elsa.CodeGeneration.Build");

        // Publish both assets from the same two-target restore to catch intermediate or lock overwrites.
        string[] frameworks = BuildTest.Framework == "net11.0" && !disableRuntimeAsync ? ["net10.0", "net11.0"] : [BuildTest.Framework];
        foreach (var framework in frameworks)
        {
            var lane = Path.Combine(evidence, framework);
            var publish = Path.Combine(lane, "publish");
            var properties = await ReadBuildProperties(consumer, ConsumerProject, Path.Combine(lane, "consumer-properties"), framework,
                [.. options, "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={build}"]);
            await VerifyCompilerSettings(properties, framework, disableRuntimeAsync);
            var result = await Run($"{framework}/publish", "dotnet", ["publish", ConsumerProject, "--framework", framework, "--no-restore",
                "--artifacts-path", build, "--output", publish, $"-bl:{Path.Combine(lane, "publish.binlog")}", .. options]);
            result.EnsureSuccess();
            await Assert.That(result.Output).DoesNotMatch(@"\b(?:warning|error) IL\d+\b");

            var executable = Path.Combine(publish, OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer");
            var run = await Run($"{framework}/execute", executable, "--expect-mode", mode);
            run.EnsureSuccess();
            await Assert.That(ModulePackageEvidence.IsPassingProtocol(run.Output, mode, framework)).IsTrue().Because(run.Output);
            var injected = await Run($"{framework}/injected-failure", executable, "--expect-mode", mode, "--self-test-failure");
            await Assert.That(injected.ExitCode).IsEqualTo(1);
            await Assert.That(injected.Output).Contains("Injected packed consumer assertion failure");
            await Assert.That(injected.Output).DoesNotContain("probe=module-packages result=passed");
        }
    }

    private static async Task<JsonElement> ReadBuildProperties(string directory, string project, string destination, string framework, string[] options)
    {
        var result = await BuildTest.Run(directory, destination + ".log", "dotnet", ["msbuild", project, "-nologo", $"-p:TargetFramework={framework}",
            .. options, "-getProperty:Configuration,TargetFramework,UseRuntimeAsync,Features,WeaverConfiguration,IsPackable,PackageId,PackageVersion",
            $"-getResultOutputFile:{destination}.json"]);
        result.EnsureSuccess();
        using var file = JsonDocument.Parse(File.ReadAllBytes(destination + ".json"));
        return file.RootElement.GetProperty("Properties").Clone();
    }

    private static async Task VerifyCompilerSettings(JsonElement properties, string framework, bool disableRuntimeAsync)
    {
        await Assert.That(properties.GetProperty("Configuration").GetString()).IsEqualTo("Release");
        await Assert.That(properties.GetProperty("TargetFramework").GetString()).IsEqualTo(framework);
        var enabled = framework == "net11.0" && !disableRuntimeAsync;
        var property = properties.GetProperty("UseRuntimeAsync").GetString()!;
        var features = properties.GetProperty("Features").GetString()!;
        await Assert.That(ModulePackageEvidence.HasRuntimeAsyncSettings(property, features, enabled)).IsTrue()
            .Because($"Expected runtime async enabled={enabled} on {framework}, observed UseRuntimeAsync={property}, Features={features}.");
        await Assert.That(properties.GetProperty("WeaverConfiguration").GetString()).Contains("ContinueOnCapturedContext=\"false\"");
    }
}
