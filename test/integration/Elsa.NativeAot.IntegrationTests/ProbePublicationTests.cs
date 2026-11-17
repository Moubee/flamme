using System.Text.RegularExpressions;

namespace Elsa.NativeAot.IntegrationTests;

[Explicit]
[NotInParallel("NativeAotBuild")]
public class ProbePublicationTests
{
    public static IEnumerable<(string Mode, bool DisableRuntimeAsync)> PublicationModes()
    {
        foreach (var mode in new[] { "managed", "native" })
        {
            yield return (mode, false);
#if NET11_0_OR_GREATER
            yield return (mode, true);
#endif
        }
    }

    public static IEnumerable<(string Probe, string Mode, bool DisableRuntimeAsync)> LegacyProbeCases()
    {
        foreach (var probe in new[] { "features", "execution", "payloads", "integrated" })
        foreach (var (mode, disableRuntimeAsync) in PublicationModes())
            yield return (probe, mode, disableRuntimeAsync);
    }

    [Test, Category("NativeAotAcceptance")]
    [MethodDataSource(nameof(PublicationModes))]
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
                (await Run(name + "-properties", "dotnet", ["msbuild", path, "-nologo", .. properties,
                    $"-p:TargetFramework={BuildTest.Framework}", "-p:Configuration=Release", "-p:UseArtifactsOutput=true",
                    $"-p:ArtifactsPath={build}", $"-getProperty:{evaluated}"])).EnsureSuccess();
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
