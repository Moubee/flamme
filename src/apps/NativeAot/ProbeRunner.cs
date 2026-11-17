using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.NativeAot;

internal static class ProbeRunner
{
#if NATIVEAOT_PROBE_NATIVE
    private const string BuildMode = "native";
#else
    private const string BuildMode = "managed";
#endif

    public static async Task<int> RunAsync(string[] args, string probe, Func<Task> scenario, string provider = "legacy-baseline")
    {
        try
        {
            Require(provider is "legacy-baseline" or "explicit-catalog", "Provider must be legacy-baseline or explicit-catalog.");
            Require(provider != "explicit-catalog" || probe == "features", "The explicit catalog is supported only by the features probe.");
            var expectedMode = BuildMode;
            var controlOnly = false;
            var injectFailure = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--expect-mode" when i + 1 < args.Length:
                        expectedMode = args[++i];
                        Require(expectedMode is "managed" or "native", "Expected mode must be managed or native.");
                        break;
                    case "--control-only":
                        controlOnly = true;
                        break;
                    case "--self-test-failure":
                        injectFailure = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
                }
            }

            Console.WriteLine($"probe={probe} provider={provider} build-mode={BuildMode} expected-mode={expectedMode}");
            Console.WriteLine($"framework={RuntimeInformation.FrameworkDescription} rid={RuntimeInformation.RuntimeIdentifier} dynamic-code={RuntimeFeature.IsDynamicCodeSupported} reflection-json={JsonSerializer.IsReflectionEnabledByDefault}");
            Require(expectedMode == BuildMode, "Requested mode differs from the compiled probe mode.");
            Require((expectedMode == "native") == !RuntimeFeature.IsDynamicCodeSupported, "Execution mode does not match dynamic-code support. Publish and run the requested binary.");
            Require(!JsonSerializer.IsReflectionEnabledByDefault, "Reflection JSON must be disabled.");
            Require(!injectFailure, "Injected harness assertion failure.");

            // Independent positive control: exercise generated JSON and a genuinely asynchronous await.
            await Task.Delay(1);
            var fixture = new ProbeControl("native-aot-control", 42);
            var json = JsonSerializer.Serialize(fixture, ProbeControlContext.Default.ProbeControl);
            var result = JsonSerializer.Deserialize(json, ProbeControlContext.Default.ProbeControl);
            Require(result == fixture, "Generated JSON control did not round-trip.");
            Console.WriteLine("scenario=harness-control result=passed");

            if (!controlOnly)
            {
                Console.WriteLine($"scenario={probe} result=started");
                await scenario();
                Console.WriteLine($"scenario={probe} result=passed");
            }

            Console.WriteLine($"probe={probe} result=passed scope={(controlOnly ? "harness-control" : provider)}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"probe={probe} result=failed exception={exception.GetType().FullName} message={exception.Message}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    public static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

internal sealed record ProbeControl(string Name, int Value);

[JsonSerializable(typeof(ProbeControl))]
internal partial class ProbeControlContext : JsonSerializerContext;
