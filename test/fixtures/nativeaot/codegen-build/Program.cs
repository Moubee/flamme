using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Elsa.CodeGeneration;
using Elsa.Features.Models;
using Elsa.NativeAot.Modules.Producer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

try
{
    if (args.Length != 4 || args[0] != "--expect-provider" || args[2] != "--expect-native")
        throw new ArgumentException("Expected --expect-provider Dynamic|Generated --expect-native true|false.");
    var expectedProvider = args[1];
    var expectedNative = bool.Parse(args[3]);
    var services = new ServiceCollection();
    var module = ElsaHost.CreateModule(services, CompositionPolicy.ValidatedV1);
    var state = new ProducerState();
    module.Properties[ProducerState.StatePropertyKey] = state;
    module.Configure(m => new FoundationFeature(m), feature => feature.Value = "sdk-consumer");
    module.Apply();
    if (FixtureSelection.Provider != expectedProvider)
        throw new InvalidOperationException($"Expected {expectedProvider}, executed {FixtureSelection.Provider}.");
    if (expectedNative != !RuntimeFeature.IsDynamicCodeSupported || JsonSerializer.IsReflectionEnabledByDefault)
        throw new InvalidOperationException("Publication mode or JSON reflection flag is inconsistent.");
    await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    var hosted = provider.GetServices<IHostedService>().Single();
    await hosted.StartAsync(CancellationToken.None);
    await hosted.StopAsync(CancellationToken.None);
    if (state.Constructors != 1 || state.Configurations != 1 || state.Applications != 1 || state.Starts != 1 || state.Stops != 1)
        throw new InvalidOperationException("The selected provider did not preserve the production producer lifecycle.");
    Console.WriteLine($"probe=codegen-build provider={FixtureSelection.Provider} dynamic-code={RuntimeFeature.IsDynamicCodeSupported} reflection-json={JsonSerializer.IsReflectionEnabledByDefault}");
    Console.WriteLine($"framework={RuntimeInformation.FrameworkDescription} rid={RuntimeInformation.RuntimeIdentifier}");
    Console.WriteLine("scenario=producer-lifecycle result=passed");
    Console.WriteLine("probe=codegen-build result=passed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"probe=codegen-build result=failed exception={exception.GetType().FullName} message={exception.Message}");
    return 1;
}
