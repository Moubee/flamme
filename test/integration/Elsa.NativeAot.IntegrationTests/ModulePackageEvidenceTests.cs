using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Elsa.NativeAot.IntegrationTests;

internal static class ModulePackageEvidence
{
    public static bool IsPassingProtocol(string output, string mode, string framework)
    {
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines is [var header, var runtime, "probe=module-packages result=passed scope=packed-modules"]
            && header == $"probe=module-packages provider=packed-modules build-mode={mode} expected-mode={mode}"
            && Regex.IsMatch(runtime, $@"^framework=\.NET {Regex.Escape(framework[3..])}\.\S+ rid={Regex.Escape(BuildTest.Rid)} dynamic-code={mode == "managed"} reflection-json=False$");
    }

    public static bool HasRuntimeAsyncSettings(string property, string features, bool enabled)
    {
        var asyncFeatures = Regex.Split(features, @"[,;\s]+")
            .Where(feature => feature.Split('=')[0].Equals("runtime-async", StringComparison.OrdinalIgnoreCase));
        return bool.TryParse(property, out var propertyValue) && propertyValue == enabled
            && asyncFeatures.SequenceEqual(enabled ? ["runtime-async=on"] : []);
    }
}

public class ModulePackageEvidenceTests
{
    [Test]
    public async Task PackageSourceMappingPreservesFeedPathAndExactLocalIds()
    {
        var feed = Path.Combine(Path.GetTempPath(), """feed & "double" 'single' <xml>""");
        var config = XElement.Parse(PackageFeed.CreateNuGetConfig(feed).ToString());
        var source = config.Element("packageSources")!.Elements("add").Single(e => (string?)e.Attribute("key") == "fixture");
        await Assert.That((string?)source.Attribute("value")).IsEqualTo(feed);
        var mappings = config.Element("packageSourceMapping")!.Elements("packageSource");
        var fixture = mappings.Single(e => (string?)e.Attribute("key") == "fixture");
        var packageIds = fixture.Elements("package").Select(e => (string)e.Attribute("pattern")!).Order().ToArray();
        string[] expected = ["Elsa.CodeGeneration", "Elsa.CodeGeneration.Abstractions", "Elsa.CodeGeneration.Analyzers",
            "Elsa.CodeGeneration.Build", "Elsa.Features", "Elsa.NativeAot.Modules.Producer"];
        await Assert.That(packageIds.SequenceEqual(expected)).IsTrue();
        var official = mappings.Single(e => (string?)e.Attribute("key") == "nuget.org");
        await Assert.That((string?)official.Elements("package").Single().Attribute("pattern")).IsEqualTo("*");
    }

    [Test]
    [Arguments("true", ";unrelated=on;runtime-async=on", true, true)]
    [Arguments("true", ";unrelated-runtime-async=on", true, false)]
    [Arguments("false", "", true, false)]
    [Arguments("false", "", false, true)]
    [Arguments("true", "runtime-async=on", false, false)]
    [Arguments("false", "runtime-async=on", false, false)]
    public async Task RequiresTheMatchingPropertyAndCompilerFeature(string property, string features, bool enabled, bool matches)
    {
        await Assert.That(ModulePackageEvidence.HasRuntimeAsyncSettings(property, features, enabled)).IsEqualTo(matches);
    }

    [Test]
    public async Task RequiresThePackedProviderAndRequestedFramework()
    {
        var output = $"""
            probe=module-packages provider=tampered build-mode=native expected-mode=native
            framework=.NET 10.0.0 rid={BuildTest.Rid} dynamic-code=False reflection-json=False
            probe=module-packages result=passed scope=packed-modules
            """;
        await Assert.That(ModulePackageEvidence.IsPassingProtocol(output, "native", "net10.0")).IsFalse();
        var valid = output.Replace("provider=tampered", "provider=packed-modules");
        await Assert.That(ModulePackageEvidence.IsPassingProtocol(valid, "native", "net10.0")).IsTrue();
        await Assert.That(ModulePackageEvidence.IsPassingProtocol(valid, "native", "net11.0")).IsFalse();
    }
}
