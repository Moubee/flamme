using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Elsa.NativeAot.ModuleConsumer;

return await PackageConsumer.RunAsync(args);

internal static class PackageConsumer
{
#if PACKED_CONSUMER_NATIVE
    private const string BuildMode = "native";
#else
    private const string BuildMode = "managed";
#endif

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var expectedMode = BuildMode;
            var injectFailure = false;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--expect-mode" when index + 1 < args.Length:
                        expectedMode = args[++index];
                        break;
                    case "--self-test-failure":
                        injectFailure = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {args[index]}");
                }
            }

            Console.WriteLine($"probe=module-packages provider=packed-modules build-mode={BuildMode} expected-mode={expectedMode}");
            Console.WriteLine($"framework={RuntimeInformation.FrameworkDescription} rid={RuntimeInformation.RuntimeIdentifier} dynamic-code={RuntimeFeature.IsDynamicCodeSupported} reflection-json={JsonSerializer.IsReflectionEnabledByDefault}");
            Require(expectedMode == BuildMode && expectedMode is "managed" or "native", "The expected mode differs from the compiled consumer.");
            Require((BuildMode == "native") == !RuntimeFeature.IsDynamicCodeSupported, "Dynamic-code support disagrees with the published mode.");
            Require(!JsonSerializer.IsReflectionEnabledByDefault, "Reflection JSON must remain disabled.");
            Require(!injectFailure, "Injected packed consumer assertion failure.");

            await ModuleConsumerScenarios.RunAsync();
            Console.WriteLine("probe=module-packages result=passed scope=packed-modules");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"probe=module-packages result=failed exception={exception.GetType().FullName} message={exception.Message}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
