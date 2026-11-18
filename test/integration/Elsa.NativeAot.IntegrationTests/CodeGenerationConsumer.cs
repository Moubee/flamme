using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

internal sealed class CodeGenerationConsumer
{
    private readonly PackageFeed _feed;
    private readonly string _source;
    private readonly string _evidence;
    private readonly bool _sourceProducer;
    private readonly bool _compiledFeatures;
    private int _step;

    private CodeGenerationConsumer(PackageFeed feed, string source, string evidence, bool sourceProducer, bool compiledFeatures)
    {
        _feed = feed;
        _source = source;
        _evidence = evidence;
        _sourceProducer = sourceProducer;
        _compiledFeatures = compiledFeatures;
    }

    private string BuildDirectory => Path.Combine(_evidence, "build");
    private string IntermediateDirectory => Path.Combine(BuildDirectory, "obj", "Consumer");
    private string PublishDirectory => Path.Combine(_evidence, "publish");
    private static string Template(string file) => Path.Combine(BuildTest.Root, "test", "fixtures", "nativeaot", "codegen-build", file);

    public static async Task<CodeGenerationConsumer> Create(string name, bool manual = false, bool sourceLibrary = false,
        bool localMode = false, bool lateMode = false, bool generatedFilename = false, bool compiledFeatures = false,
        bool disableRuntimeAsync = false, bool unsafeProducer = false)
    {
        var feed = await PackageFeed.Get(disableRuntimeAsync);
        var source = feed.PrepareConsumer(name);
        var evidence = BuildTest.CreateDirectory(name);
        var project = XDocument.Load(Template("Consumer.csproj.in"));
        var properties = project.Root!.Element("PropertyGroup")!;

        if (disableRuntimeAsync)
            properties.Add(new XElement("UseRuntimeAsync", false));
        if (manual)
            properties.Add(new XElement("ElsaHostBootstrap", "Manual"));
        if (localMode)
            properties.Add(new XElement("ElsaExecutionMode", "Generated"));
        if (lateMode)
            new XDocument(new XElement("Project", new XElement("PropertyGroup", new XElement("ElsaExecutionMode", "Generated"))))
                .Save(Path.Combine(source, "Directory.Build.targets"));

        if (sourceLibrary)
            project.Descendants("PackageReference").Single(p => (string?)p.Attribute("Include") == PackageFeed.Producer).Remove();
        if (!compiledFeatures)
            project.Root.Add(XElement.Load(Template("ExplicitTooling.props.in")).Elements());
        project.Save(Path.Combine(source, "Consumer.csproj"));
        File.Copy(Template(compiledFeatures ? "CompiledFeatures.cs.in" : "Program.cs"), Path.Combine(source, "Program.cs"));
        if (!compiledFeatures)
            File.Copy(Template(manual ? "ElsaHost.Manual.cs.in" : "ElsaHost.Compiler.cs.in"),
                Path.Combine(source, generatedFilename ? "ElsaHost.g.cs" : "ElsaHost.cs"));

        if (sourceLibrary || unsafeProducer)
        {
            var library = Directory.CreateDirectory(Path.Combine(source, "SourceLibrary")).FullName;
            File.Copy(Template("SourceLibrary.csproj.in"), Path.Combine(library, "SourceLibrary.csproj"));
            if (unsafeProducer)
                File.Copy(Template("UnsafeFeature.cs.in"), Path.Combine(library, "UnsafeFeature.cs"));
            else
                File.Copy(Path.Combine(BuildTest.Root, "test/fixtures", PackageFeed.Producer, "ProducerExports.cs"), Path.Combine(library, "ProducerExports.cs"));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var snapshot = Path.Combine(evidence, "source", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            File.Copy(file, snapshot);
        }

        return new CodeGenerationConsumer(feed, source, evidence, sourceLibrary, compiledFeatures);
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
        string[] omitted = _compiledFeatures ? PackageFeed.CompilerPackages :
            settings.Contains("-p:FixtureIncludeGenerator=false") ? ["Elsa.CodeGeneration"] : [];
        if (_sourceProducer)
            omitted = [.. omitted, PackageFeed.Producer];
        var hasSourceLibrary = Directory.Exists(Path.Combine(_source, "SourceLibrary"));
        if (hasSourceLibrary)
            _feed.VerifyAssets(Path.Combine(BuildDirectory, "obj", "SourceLibrary", "project.assets.json"), false,
                [PackageFeed.Producer, .. PackageFeed.CompilerPackages], ["Elsa.Features"]);
        string[]? directPackages = _sourceProducer ? [] : _compiledFeatures ? [PackageFeed.Producer] : null;
        _feed.VerifyAssets(Path.Combine(IntermediateDirectory, "project.assets.json"), hasSourceLibrary, omitted, directPackages);
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
        string[] tooling = ["RegistrationCompiler", "Elsa.CodeGeneration", "Elsa.CodeGeneration.Analyzers"];
        foreach (var assembly in tooling)
            await Assert.That(Directory.EnumerateFiles(output, assembly + ".dll", SearchOption.AllDirectories)).IsEmpty();
        var dependencies = Path.Combine(output, "Consumer.deps.json");
        if (File.Exists(dependencies))
        {
            var manifest = File.ReadAllText(dependencies);
            await Assert.That(manifest).DoesNotContain("Microsoft.CodeAnalysis");
            foreach (var assembly in tooling)
                await Assert.That(manifest).DoesNotContain(assembly + ".dll");
        }
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

        if (_compiledFeatures)
        {
            var interceptors = Directory.GetFiles(IntermediateDirectory, "ElsaFeatureInterceptors.g.cs", SearchOption.AllDirectories);
            await Assert.That(interceptors.Length).IsEqualTo(provider == "Generated" ? 1 : 0);
            if (provider == "Generated")
            {
                var generated = File.ReadAllText(interceptors.Single());
                await Assert.That(generated).Contains("InterceptCreateModule");
                await Assert.That(generated).Contains("BuildAvailable()");
            }
        }

        // Ask the installed targets which settings reached the compiler in this lane.
        var propertiesFile = Path.Combine(_evidence, "effective-properties.json");
        var snapshot = await Run("effective-properties", "dotnet", ["msbuild", "Consumer.csproj", "-nologo", "-t:ResolveElsaCodeGenerationSettings;ResolveLockFileAnalyzers", "-getItem:Analyzer",
            "-p:Configuration=Release", $"-p:TargetFramework={BuildTest.Framework}", "-p:UseArtifactsOutput=true", $"-p:ArtifactsPath={BuildDirectory}",
            "-getProperty:ElsaExecutionMode,ElsaEffectiveExecutionMode,ElsaGenerateMetadata,ElsaIsHost,ElsaHostBootstrap,DefineConstants",
            $"-getResultOutputFile:{propertiesFile}", .. settings]);
        snapshot.EnsureSuccess();
        using var document = JsonDocument.Parse(File.ReadAllBytes(propertiesFile));
        var analyzers = document.RootElement.GetProperty("Items").GetProperty("Analyzer").EnumerateArray()
            .Select(a => a.GetProperty("Filename").GetString()).ToArray();
        var includesGenerator = !settings.Contains("-p:FixtureIncludeGenerator=false");
        await Assert.That(analyzers.Count(name => name == "Elsa.CodeGeneration")).IsEqualTo(includesGenerator ? 1 : 0);
        await Assert.That(analyzers.Count(name => name == "RegistrationCompiler")).IsEqualTo(includesGenerator ? 1 : 0);
        await Assert.That(analyzers.Count(name => name == "Elsa.CodeGeneration.Analyzers")).IsEqualTo(1);
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
