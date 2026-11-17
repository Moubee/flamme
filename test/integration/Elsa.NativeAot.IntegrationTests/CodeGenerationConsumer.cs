using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

internal sealed class CodeGenerationConsumer
{
    private readonly PackageFeed _feed;
    private readonly string _source;
    private readonly string _evidence;
    private readonly bool _hasSourceLibrary;
    private int _step;

    private CodeGenerationConsumer(PackageFeed feed, string source, string evidence, bool hasSourceLibrary)
    {
        _feed = feed;
        _source = source;
        _evidence = evidence;
        _hasSourceLibrary = hasSourceLibrary;
    }

    private string BuildDirectory => Path.Combine(_evidence, "build");
    private string IntermediateDirectory => Path.Combine(BuildDirectory, "obj", "Consumer");
    private string PublishDirectory => Path.Combine(_evidence, "publish");
    private static string Template(string file) => Path.Combine(BuildTest.Root, "test", "fixtures", "nativeaot", "codegen-build", file);

    public static async Task<CodeGenerationConsumer> Create(string name, bool manual = false, bool sourceLibrary = false,
        bool localMode = false, bool lateMode = false, bool generatedFilename = false)
    {
        var feed = await PackageFeed.Get();
        var source = feed.PrepareConsumer(name);
        var evidence = BuildTest.CreateDirectory(name);
        var project = XDocument.Load(Template("Consumer.csproj.in"));
        var properties = project.Root!.Element("PropertyGroup")!;

        if (manual)
            properties.Add(new XElement("ElsaHostBootstrap", "Manual"));
        if (localMode)
            properties.Add(new XElement("ElsaExecutionMode", "Generated"));
        if (lateMode)
            new XDocument(new XElement("Project", new XElement("PropertyGroup", new XElement("ElsaExecutionMode", "Generated"))))
                .Save(Path.Combine(source, "Directory.Build.targets"));

        project.Save(Path.Combine(source, "Consumer.csproj"));
        File.Copy(Template("Program.cs"), Path.Combine(source, "Program.cs"));
        File.Copy(Template(manual ? "ElsaHost.Manual.cs.in" : "ElsaHost.Compiler.cs.in"),
            Path.Combine(source, generatedFilename ? "ElsaHost.g.cs" : "ElsaHost.cs"));

        if (sourceLibrary)
        {
            var library = Directory.CreateDirectory(Path.Combine(source, "SourceLibrary")).FullName;
            File.Copy(Template("SourceLibrary.csproj.in"), Path.Combine(library, "SourceLibrary.csproj"));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var snapshot = Path.Combine(evidence, "source", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            File.Copy(file, snapshot);
        }

        return new CodeGenerationConsumer(feed, source, evidence, sourceLibrary);
    }

    private Task<CommandResult> Run(string name, string executable, params string[] args) =>
        BuildTest.Run(_source, Path.Combine(_evidence, $"{++_step:D2}-{name}.log"), executable, args);

    public Task<CommandResult> Restore(params string[] settings) =>
        Run("restore", "dotnet", ["restore", "Consumer.csproj", "--force-evaluate", "--artifacts-path", BuildDirectory, .. settings]);

    public async Task RestoreLocked(string[] settings)
    {
        (await Restore(settings)).EnsureSuccess();
        PackageFeed.VerifyLock(Path.Combine(_source, "packages.lock.json"));
        (await Run("restore-locked", "dotnet", ["restore", "Consumer.csproj", "--locked-mode", "--artifacts-path", BuildDirectory, .. settings])).EnsureSuccess();
        string[] omitted = settings.Contains("-p:FixtureIncludeGenerator=false") ? ["Elsa.CodeGeneration"] : [];
        _feed.VerifyAssets(Path.Combine(IntermediateDirectory, "project.assets.json"), _hasSourceLibrary, omitted);
    }

    public Task<CommandResult> Build(string[] settings, bool native = false)
    {
        string[] publish = native
            ? ["--output", PublishDirectory, "-p:ILLinkTreatWarningsAsErrors=true", "-p:IlcTreatWarningsAsErrors=true",
                "-p:TrimmerSingleWarn=false", "-p:SuppressTrimAnalysisWarnings=false"]
            : [];

        return Run("build", "dotnet", [native ? "publish" : "build", "Consumer.csproj", "-c", "Release", "-f", BuildTest.Framework,
            "--no-restore", "--artifacts-path", BuildDirectory, "-p:EmitCompilerGeneratedFiles=true", $"-bl:{Path.Combine(_evidence, $"build-{_step + 1}.binlog")}",
            .. settings, .. publish]);
    }

    private string ExecutableDirectory(bool native) =>
        native ? PublishDirectory : Path.Combine(BuildDirectory, "bin", "Consumer", $"release_{BuildTest.Framework}");

    public Task<CommandResult> Execute(string provider, bool native = false)
    {
        var output = ExecutableDirectory(native);
        string[] arguments = ["--expect-provider", provider, "--expect-native", native ? "true" : "false"];
        return native
            ? Run("execute", Path.Combine(output, OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer"), arguments)
            : Run("execute", "dotnet", [Path.Combine(output, "Consumer.dll"), .. arguments]);
    }

    public async Task VerifyExecution(string provider, bool native = false)
    {
        var result = await Execute(provider, native);
        result.EnsureSuccess();
        var lines = result.Output.TrimEnd().Split(Environment.NewLine);
        await Assert.That(lines.Length).IsEqualTo(4);
        await Assert.That(lines[0]).IsEqualTo($"probe=codegen-build provider={provider} dynamic-code={!native} reflection-json=False");
        await Assert.That(lines[1]).StartsWith($"framework=.NET {BuildTest.Framework[3..]}.");
        await Assert.That(lines[1]).EndsWith($" rid={BuildTest.Rid}");
        await Assert.That(lines[2]).IsEqualTo("scenario=producer-lifecycle result=passed");
        await Assert.That(lines[3]).IsEqualTo("probe=codegen-build result=passed");

        var output = ExecutableDirectory(native);
        await Assert.That(Directory.EnumerateFiles(output, "Microsoft.CodeAnalysis*.dll")).IsEmpty();
        var dependencies = Path.Combine(output, "Consumer.deps.json");
        if (File.Exists(dependencies))
            await Assert.That(File.ReadAllText(dependencies)).DoesNotContain("Microsoft.CodeAnalysis");
    }

    public async Task Verify(string provider, string[] settings, bool native = false)
    {
        if (native)
            settings = [.. settings, "-p:PublishAot=true", $"-p:RuntimeIdentifier={BuildTest.Rid}"];

        await RestoreLocked(settings);
        var result = await Build(settings, native);
        result.EnsureSuccess();

        var diagnostics = Regex.Matches(result.Output, @"\b(?:warning|error) (?:CS878[45]|CS803[24]|AD0001|IL\d+)\b");
        await Assert.That(diagnostics.Where(match => native || !match.Value.Contains(" IL"))).IsEmpty();
        await VerifyExecution(provider, native);

        // Ask the installed targets which settings reached the compiler in this lane.
        var propertiesFile = Path.Combine(_evidence, "effective-properties.json");
        var snapshot = await Run("effective-properties", "dotnet", ["msbuild", "Consumer.csproj", "-nologo", "-t:ResolveElsaCodeGenerationSettings",
            "-p:Configuration=Release", $"-p:TargetFramework={BuildTest.Framework}", "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={BuildDirectory}",
            "-getProperty:ElsaExecutionMode,ElsaEffectiveExecutionMode,ElsaGenerateMetadata,ElsaIsHost,ElsaHostBootstrap,DefineConstants",
            $"-getResultOutputFile:{propertiesFile}", .. settings]);
        snapshot.EnsureSuccess();
        using var document = JsonDocument.Parse(File.ReadAllBytes(propertiesFile));
        var properties = document.RootElement.GetProperty("Properties");
        await Assert.That(properties.GetProperty("ElsaEffectiveExecutionMode").GetString()).IsEqualTo(provider);

        var generationEnabled = bool.Parse(properties.GetProperty("ElsaGenerateMetadata").GetString()!);
        var markers = Directory.GetFiles(IntermediateDirectory, "ElsaGenerationBuildMarker.g.cs", SearchOption.AllDirectories);
        await Assert.That(markers.Length).IsEqualTo(generationEnabled ? 1 : 0);
        if (generationEnabled)
        {
            var source = File.ReadAllText(markers.Single());
            await Assert.That(source).Contains("class ElsaGenerationBuildMarker");
            await Assert.That(source).Contains($"const string EffectiveExecutionMode = \"{provider}\"");
        }
    }

    public async Task VerifyChildMode(string expected)
    {
        var observed = File.ReadAllLines(Path.Combine(_source, "SourceLibrary", "observed-mode.txt"));
        File.WriteAllLines(Path.Combine(_evidence, "observed-child.txt"), observed);
        await Assert.That(observed).Contains($"framework={BuildTest.Framework}");
        await Assert.That(observed).Contains($"mode={expected}");
        await Assert.That(observed).Contains("host=false");
        // The first constant is prefixed by "constants=" in MSBuild's output.
        await Assert.That(string.Join('\n', observed)).DoesNotMatch(@"\b(?:ELSA_GENERATED|ELSA_DYNAMIC)\b");
    }
}
