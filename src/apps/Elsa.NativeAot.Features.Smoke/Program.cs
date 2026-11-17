#if EXPLICIT_FEATURE_CATALOG
using Elsa.NativeAot;

return await ProbeRunner.RunAsync(args, "features", async () =>
{
    await ExplicitCompositionScenarios.RunAsync();
    await ExplicitHostedServiceScenarios.RunAsync();
    Console.WriteLine("CHECK features.versioned-module-exports");
    await Elsa.NativeAot.ModuleConsumer.ModuleConsumerScenarios.RunAsync();
}, provider: "explicit-catalog");
#else
using Elsa.Features.Abstractions;
using Elsa.Features.Attributes;
using Elsa.Features.Contracts;
using Elsa.Features.Implementations;
using Elsa.Features.Services;
using Elsa.NativeAot;
using Microsoft.Extensions.DependencyInjection;

return await ProbeRunner.RunAsync(args, "features", () =>
{
    var trace = new List<string>();
    var module = new Module(new ServiceCollection());
    module.Properties["trace"] = trace;

    Console.WriteLine("CHECK features.default-activation");
    module.Configure<RootFeature>();
    Console.WriteLine("CHECK features.dependencies-and-lifecycle");
    module.Apply();

    ProbeRunner.Require(trace.SequenceEqual(["dependency.configure", "root.configure", "dependency.apply", "root.apply"]),
        $"Unexpected feature lifecycle: {string.Join(",", trace)}");
    var registry = module.Services.Single(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance;
    ProbeRunner.Require(registry is IInstalledFeatureRegistry installed && installed.Find("Elsa.Root") is not null,
        "The installed feature registry must contain Root.");
    return Task.CompletedTask;
});

[DependsOn(typeof(DependencyFeature))]
public sealed class RootFeature(IModule module) : FeatureBase(module)
{
    public override void Configure() => ((List<string>)Module.Properties["trace"]).Add("root.configure");
    public override void Apply() => ((List<string>)Module.Properties["trace"]).Add("root.apply");
}

public sealed class DependencyFeature(IModule module) : FeatureBase(module)
{
    public override void Configure() => ((List<string>)Module.Properties["trace"]).Add("dependency.configure");
    public override void Apply() => ((List<string>)Module.Properties["trace"]).Add("dependency.apply");
}
#endif