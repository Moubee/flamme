using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

[Explicit]
[NotInParallel("NativeAotBuild")]
public class ProbePublicationTests
{
    private static IEnumerable<(string Mode, bool DisableRuntimeAsync)> PublicationModes()
    {
        foreach (var mode in new[] { "managed", "native" })
        {
            yield return (mode, false);
#if NET11_0_OR_GREATER
            yield return (mode, true);
#endif
        }
    }

    public static IEnumerable<TestDataRow<(string, bool)>> ExplicitProbeCases()
    {
        foreach (var (mode, disabled) in PublicationModes())
        {
            var lane = $"explicit-catalog-features-{mode}-{(disabled ? "disabled" : "default")}";
            yield return new((mode, disabled), DisplayName: lane, Categories: [lane]);
        }
    }

    public static IEnumerable<TestDataRow<(string, string, bool)>> LegacyProbeCases()
    {
        foreach (var probe in new[] { "features", "execution", "payloads", "integrated" })
        foreach (var (mode, disabled) in PublicationModes())
        {
            var lane = $"legacy-baseline-{probe}-{mode}-{(disabled ? "disabled" : "default")}";
            yield return new((probe, mode, disabled), DisplayName: lane, Categories: [lane]);
        }
    }

    [Test, Category("NativeAotAcceptance")]
    [MethodDataSource(nameof(ExplicitProbeCases))]
    public Task ExplicitCatalogRunsPublishedScenario(string mode, bool disableRuntimeAsync) =>
        PublishAndRun("features", "explicit-catalog", mode, disableRuntimeAsync);

    [Test, Category("NativeAotDiagnostic")]
    [MethodDataSource(nameof(LegacyProbeCases))]
    public Task LegacyProbeRunsPublishedScenario(string probe, string mode, bool disableRuntimeAsync) =>
        PublishAndRun(probe, "legacy-baseline", mode, disableRuntimeAsync);

    private static async Task PublishAndRun(string probe, string provider, string mode, bool disableRuntimeAsync)
    {
        var app = probe switch
        {
            "features" => "Elsa.NativeAot.Features.Smoke",
            "execution" => "Elsa.NativeAot.Execution.Smoke",
            "payloads" => "Elsa.NativeAot.Payloads.Smoke",
            "integrated" => "Elsa.NativeAot.Smoke",
            _ => throw new ArgumentOutOfRangeException(nameof(probe))
        };
        var project = Path.Combine(BuildTest.Root, "src", "apps", app, $"{app}.csproj");
        var library = Path.Combine(BuildTest.Root, probe == "features"
            ? "src/common/Elsa.Features/Elsa.Features.csproj"
            : "src/modules/Elsa.Workflows.Core/Elsa.Workflows.Core.csproj");
        var artifacts = BuildTest.CreateDirectory($"{provider}-{probe}-{mode}-{BuildTest.Rid}-{(disableRuntimeAsync ? "disabled" : "default")}");
        var build = Path.Combine(artifacts, "build");
        var publish = Path.Combine(artifacts, "publish");
        var strict = bool.Parse(Environment.GetEnvironmentVariable("ELSA_NATIVEAOT_STRICT") ?? "false");
        var properties = new List<string>
        {
            $"-p:NativeAotMode={mode}",
            $"-p:NativeAotFeatureProvider={(provider == "explicit-catalog" ? "explicit" : "legacy")}",
            $"-p:RuntimeIdentifier={BuildTest.Rid}",
            "-p:EnableTrimAnalyzer=true", "-p:EnableAotAnalyzer=true", "-p:NoWarn=CS1591",
            "-p:TrimmerSingleWarn=false", "-p:SuppressTrimAnalysisWarnings=false",
            $"-p:ILLinkTreatWarningsAsErrors={strict}", $"-p:IlcTreatWarningsAsErrors={strict}"
        };
        if (disableRuntimeAsync)
            properties.Add("-p:UseRuntimeAsync=false");

        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        async Task<CommandResult> Run(string phase, string executable, params string[] args)
        {
            var result = await BuildTest.Run(BuildTest.Root, Path.Combine(artifacts, phase + ".log"), executable, args);
            foreach (var line in result.Output.Split('\n'))
            {
                if (Regex.IsMatch(line, @"\b(warning|error) IL\d+\b"))
                    diagnostics.Add(line.TrimEnd('\r'));
            }
            return result;
        }

        try
        {
            (await Run("dotnet-info", "dotnet", "--info")).EnsureSuccess();
            if (mode == "native")
                (await Run("native-toolchain", "clang", "--version")).EnsureSuccess();

            // The committed lock describes both supported frameworks.
            (await Run("restore", "dotnet", ["restore", project, "--locked-mode", "--artifacts-path", build, .. properties])).EnsureSuccess();
            const string evaluated = "TargetFramework,TargetFrameworks,RuntimeIdentifier,NativeAotMode,NativeAotFeatureProvider,PublishAot,JsonSerializerIsReflectionEnabledByDefault,EnableTrimAnalyzer,EnableAotAnalyzer,NoWarn,TrimmerSingleWarn,SuppressTrimAnalysisWarnings,ILLinkTreatWarningsAsErrors,IlcTreatWarningsAsErrors,NuGetLockFilePath,UseRuntimeAsync,Features,WeaverConfiguration,ArtifactsPath";
            foreach (var (name, path) in new[] { ("probe", project), ("library", library) })
            {
                var propertiesFile = Path.Combine(artifacts, name + "-properties.json");
                (await Run(name + "-properties", "dotnet", ["msbuild", path, "-nologo", .. properties,
                    $"-p:TargetFramework={BuildTest.Framework}", "-p:Configuration=Release", "-p:UseArtifactsOutput=true",
                    $"-p:ArtifactsPath={build}", $"-getProperty:{evaluated}", $"-getResultOutputFile:{propertiesFile}"])).EnsureSuccess();
                using var snapshot = JsonDocument.Parse(File.ReadAllBytes(propertiesFile));
                var evaluatedProperties = snapshot.RootElement.GetProperty("Properties");
                string Get(string key) => evaluatedProperties.GetProperty(key).GetString()!;
                await Assert.That(Get("TargetFramework")).IsEqualTo(BuildTest.Framework);
                await Assert.That(Get("RuntimeIdentifier")).IsEqualTo(BuildTest.Rid);
                await Assert.That(Get("NativeAotMode")).IsEqualTo(mode);
                await Assert.That(Get("NativeAotFeatureProvider")).IsEqualTo(provider == "explicit-catalog" ? "explicit" : "legacy");
                var runtimeAsync = BuildTest.Framework == "net11.0" && !disableRuntimeAsync;
                await Assert.That(bool.Parse(Get("UseRuntimeAsync"))).IsEqualTo(runtimeAsync);
                await Assert.That(Get("Features").Split(';').Contains("runtime-async=on")).IsEqualTo(runtimeAsync);
                var weaver = XElement.Parse(Get("WeaverConfiguration")).Element("ConfigureAwait");
                await Assert.That((string?)weaver?.Attribute("ContinueOnCapturedContext")).IsEqualTo("false");
                if (name == "probe")
                {
                    await Assert.That(string.Equals(Get("PublishAot"), "true", StringComparison.OrdinalIgnoreCase)).IsEqualTo(mode == "native");
                    await Assert.That(Get("JsonSerializerIsReflectionEnabledByDefault")).IsEqualTo("false");
                    var expectedLock = Path.Combine(Path.GetDirectoryName(path)!, "locks", mode, BuildTest.Rid, "packages.lock.json");
                    await Assert.That(Get("NuGetLockFilePath")).IsEqualTo(expectedLock);
                }
                else
                    await Assert.That(Get("NuGetLockFilePath")).IsEmpty();
            }
            (await Run("publish", "dotnet", ["publish", project, "-c", "Release", "-f", BuildTest.Framework, "--no-restore",
                "--artifacts-path", build, "-o", publish, $"-bl:{Path.Combine(artifacts, "publish.binlog")}", .. properties])).EnsureSuccess();
            var executable = Path.Combine(publish, app);
            var result = await Run("execute", executable, "--expect-mode", mode);
            result.EnsureSuccess();
            await Assert.That(ProbeProtocol.IsComplete(result.Output, probe, provider, mode, BuildTest.Framework, BuildTest.Rid))
                .IsTrue().Because($"the published process must complete the selected scenario; see {result.Log}");

            if (provider == "explicit-catalog")
            {
                var control = await Run("control-only", executable, "--control-only");
                control.EnsureSuccess();
                await Assert.That(control.Output).Contains("result=passed scope=harness-control");
                await Assert.That(control.Output).DoesNotContain($"scenario={probe} result=started");
                var failure = await Run("self-test-failure", executable, "--self-test-failure");
                await Assert.That(failure.ExitCode).IsEqualTo(1);
                await Assert.That(failure.Output).Contains("Injected harness assertion failure.");
                var wrongMode = await Run("wrong-mode", executable, "--expect-mode", mode == "managed" ? "native" : "managed");
                await Assert.That(wrongMode.ExitCode).IsEqualTo(1);
                await Assert.That(wrongMode.Output).Contains("Requested mode differs from the compiled probe mode.");
            }
            if (strict)
                await Assert.That(diagnostics).IsEmpty().Because($"strict publication must be free of IL diagnostics; see {artifacts}");
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(artifacts, "il-diagnostics.log"), diagnostics);
        }
    }
}
