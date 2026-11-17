using System.Text.RegularExpressions;

namespace Elsa.NativeAot.IntegrationTests;

internal static class ProbeProtocol
{
    public static bool IsComplete(string output, string probe, string provider, string mode, string framework, string rid)
    {
        var lines = output.ReplaceLineEndings("\n").Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        var markers = lines.Where(line => line.StartsWith("probe=", StringComparison.Ordinal) ||
                                          line.StartsWith("scenario=", StringComparison.Ordinal) ||
                                          line.StartsWith("framework=", StringComparison.Ordinal)).ToArray();
        var final = $"probe={probe} result=passed scope={provider}";
        var major = Version.Parse(framework["net".Length..]).Major;
        var runtime = $@"^framework=\.NET {major}\.\S+ rid={Regex.Escape(rid)} dynamic-code={mode == "managed"} reflection-json=False$";
        return markers.Length == 6 && lines[^1] == final &&
               markers[0] == $"probe={probe} provider={provider} build-mode={mode} expected-mode={mode}" &&
               Regex.IsMatch(markers[1], runtime) &&
               markers[2] == "scenario=harness-control result=passed" &&
               markers[3] == $"scenario={probe} result=started" &&
               markers[4] == $"scenario={probe} result=passed" &&
               markers[5] == final;
    }
}

public class ProbeProtocolTests
{
    private const string Header = "probe=features provider=explicit-catalog build-mode=native expected-mode=native";
    private const string Runtime = "framework=.NET 11.0.0 rid=linux-x64 dynamic-code=False reflection-json=False";
    private const string Control = "scenario=harness-control result=passed";
    private const string Started = "scenario=features result=started";
    private const string Passed = "scenario=features result=passed";
    private const string Final = "probe=features result=passed scope=explicit-catalog";
    private const string Output = $"{Header}\n{Runtime}\n{Control}\n{Started}\nCHECK features.production\n{Passed}\n{Final}\n";

    private static bool Complete(string output) =>
        ProbeProtocol.IsComplete(output, "features", "explicit-catalog", "native", "net11.0", "linux-x64");

    [Test]
    public async Task CompleteProductionScenarioPasses() =>
        await Assert.That(Complete(Output)).IsTrue();

    [Test]
    [Arguments(Header, "")]
    [Arguments(Runtime, "")]
    [Arguments(Control, "")]
    [Arguments(Started, "")]
    [Arguments(Passed, "")]
    [Arguments(Final, "")]
    [Arguments("probe=features", "probe=payloads")]
    [Arguments("provider=explicit-catalog", "provider=legacy-baseline")]
    [Arguments("build-mode=native", "build-mode=managed")]
    [Arguments("expected-mode=native", "expected-mode=managed")]
    [Arguments(".NET 11", ".NET 10")]
    [Arguments("linux-x64", "osx-arm64")]
    [Arguments("dynamic-code=False", "dynamic-code=True")]
    [Arguments("reflection-json=False", "reflection-json=True")]
    [Arguments(Final, "probe=features result=passed scope=harness-control")]
    [Arguments(Final, Final + "\n" + Final)]
    [Arguments(Final, Final + "\nprobe=features result=failed")]
    [Arguments(Final, Final + "\ntrailing output")]
    [Arguments(Control + "\n" + Started, Started + "\n" + Control)]
    public async Task IncompleteOrInconsistentEvidenceFails(string original, string replacement) =>
        await Assert.That(Complete(Output.Replace(original, replacement, StringComparison.Ordinal))).IsFalse();
}
