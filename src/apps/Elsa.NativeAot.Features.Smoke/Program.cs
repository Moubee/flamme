using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Elsa.NativeAot;
using Elsa.NativeAot.ModuleConsumer;

await ExplicitCompositionScenarios.RunAsync();
await ExplicitHostedServiceScenarios.RunAsync();
Console.WriteLine("CHECK features.versioned-module-exports");
await ModuleConsumerScenarios.RunAsync();

Console.WriteLine($"result=passed provider=explicit-catalog framework=net{Environment.Version.Major}.{Environment.Version.Minor} rid={RuntimeInformation.RuntimeIdentifier} dynamic-code={RuntimeFeature.IsDynamicCodeSupported} reflection-json={JsonSerializer.IsReflectionEnabledByDefault}");
