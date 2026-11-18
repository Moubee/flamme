using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

[Explicit]
[NotInParallel("NativeAotBuild")]
public class ProbePublicationTests
{
    public static IEnumerable<TestDataRow<(string, bool)>> ExplicitProbeCases()
    {
        foreach (var mode in new[] { "managed", "native" })
        {
            var lane = $"explicit-catalog-features-{mode}-default";
            yield return new((mode, false), DisplayName: lane, Categories: [lane]);
#if NET11_0_OR_GREATER
            lane = $"explicit-catalog-features-{mode}-disabled";
            yield return new((mode, true), DisplayName: lane, Categories: [lane]);
#endif
        }
    }

    [Test, Category("NativeAotAcceptance")]
    [MethodDataSource(nameof(ExplicitProbeCases))]
    public async Task ExplicitCatalogRunsPublishedScenario(string mode, bool disableRuntimeAsync)
    {
        const string app = "Elsa.NativeAot.Features.Smoke";
        var project = Path.Combine(BuildTest.Root, "src", "apps", app, $"{app}.csproj");
        var library = Path.Combine(BuildTest.Root, "src", "common", "Elsa.Features", "Elsa.Features.csproj");
        var artifacts = BuildTest.CreateDirectory($"explicit-catalog-features-{mode}-{BuildTest.Rid}-{(disableRuntimeAsync ? "disabled" : "default")}");
        var build = Path.Combine(artifacts, "build");
        var publish = Path.Combine(artifacts, "publish");
        var strict = bool.Parse(Environment.GetEnvironmentVariable("ELSA_NATIVEAOT_STRICT") ?? "false");
        var properties = new List<string>
        {
            $"-p:NativeAotMode={mode}",
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
            const string evaluated = "TargetFramework,TargetFrameworks,RuntimeIdentifier,NativeAotMode,PublishAot,JsonSerializerIsReflectionEnabledByDefault,EnableTrimAnalyzer,EnableAotAnalyzer,NoWarn,TrimmerSingleWarn,SuppressTrimAnalysisWarnings,ILLinkTreatWarningsAsErrors,IlcTreatWarningsAsErrors,NuGetLockFilePath,UseRuntimeAsync,Features,WeaverConfiguration,ArtifactsPath";
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
            var result = await Run("execute", Path.Combine(publish, app));
            result.EnsureSuccess();
            var expected = $"result=passed provider=explicit-catalog framework={BuildTest.Framework} rid={BuildTest.Rid} dynamic-code={mode == "managed"} reflection-json=False";
            await Assert.That(result.Output.TrimEnd()).EndsWith(expected);
            if (strict)
                await Assert.That(diagnostics).IsEmpty().Because($"strict publication must be free of IL diagnostics; see {artifacts}");
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(artifacts, "il-diagnostics.log"), diagnostics);
        }
    }
}
