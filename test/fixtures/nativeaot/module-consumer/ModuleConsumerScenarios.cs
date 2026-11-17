using Elsa.CodeGeneration;
using Elsa.Features.Abstractions;
using Elsa.Features.Contracts;
using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Elsa.NativeAot.Modules.Producer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.NativeAot.ModuleConsumer;

internal static class ModuleConsumerScenarios
{
    public static async Task RunAsync()
    {
        var firstState = new ProducerState();
        var secondState = new ProducerState();
        var consumer = new ExplicitFeatureCatalogBuilder()
            .Add(module => new ConsumerFeature(module), new FeatureDescriptor("Consumer", "Fixture", "Consumer fixture"), [typeof(FoundationFeature)])
            .CreateExport(new ModuleExportDescriptor("fixture.consumer", 1,
                [new ModuleRequirement("fixture.foundation", 1, ["fixture.foundation-service"])], ["fixture.consumer-service"]));
        var catalog = new FeatureModuleCatalogBuilder()
            .Add(consumer)
            .Add(ProducerExports.Foundation)
            .Add(ProducerExports.Foundation)
            .Build();
        Require(firstState.Constructors == 0 && secondState.Constructors == 0, "Composing exports must not activate producer features.");
        Require(catalog.FeatureTypes.SequenceEqual([typeof(FoundationFeature), typeof(ConsumerFeature)]), "Module merge must put dependencies first and deduplicate the same export.");
        var firstServices = ComposeHost(catalog, firstState, "first-host");
        var secondServices = ComposeHost(catalog, secondState, "second-host");
        foreach (var state in new[] { firstState, secondState })
        {
            Require(state.Constructors == 1 && state.Configurations == 1 && state.Applications == 1, "Repeated exports must not repeat feature lifecycle callbacks.");
            Require(state.Events.SequenceEqual(["foundation.configure", "consumer.configure", "foundation.apply", "consumer.apply"]), "Producer and consumer callbacks must retain dependency order.");
            Require(state.HostedConstructors == 0, "Module composition must not construct hosted services.");
        }
        Require(firstServices.Single(x => x.ServiceType == typeof(IHostedService)).ImplementationType == typeof(FoundationHostedService), "The compiled producer must retain the hosted-service implementation identity.");

        await using var first = firstServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var second = secondServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await CheckScopesAsync(first, "first-host");
        await CheckScopesAsync(second, "second-host");
        Require(!firstState.ScopeIds.Intersect(secondState.ScopeIds).Any(), "Two hosts must not share scoped instances.");
        Require(first.GetRequiredService<ProducerState>() == firstState && second.GetRequiredService<ProducerState>() == secondState, "Each host must retain its own configuration state.");

        var firstHosted = first.GetServices<IHostedService>().Single();
        var secondHosted = second.GetServices<IHostedService>().Single();
        Require(!ReferenceEquals(firstHosted, secondHosted), "Producer hosted-service instances must be isolated between hosts.");
        Require(ReferenceEquals(firstHosted, first.GetServices<IHostedService>().Single()), "A producer hosted service must be singleton within its host.");
        await firstHosted.StartAsync(CancellationToken.None);
        await secondHosted.StartAsync(CancellationToken.None);
        await firstHosted.StopAsync(CancellationToken.None);
        Require(secondState.Stops == 0, "Stopping one host must not stop the other host.");
        await secondHosted.StopAsync(CancellationToken.None);
        foreach (var state in new[] { firstState, secondState })
        {
            Require(state.HostedConstructors == 1 && state.Starts == 1 && state.Stops == 1, "The real producer lifecycle must complete exactly once per host.");
            Require(state.ScopeIds.Count == 3 && state.ScopeIds.Distinct().Count() == 3, "Producer scopes must be recreated across operations.");
            Require(state.ScopeIds.Order().SequenceEqual(state.DisposedScopeIds.Order()), "All producer scopes must be disposed.");
        }

        var rejected = false;
        try { new FeatureModuleCatalogBuilder().Add(consumer).Build(); }
        catch (InvalidOperationException exception)
        {
            rejected = exception.Message.Contains("fixture.foundation", StringComparison.Ordinal);
        }
        Require(rejected, "An uncovered producer requirement must fail with its module identity.");
    }

    private static ServiceCollection ComposeHost(ExplicitFeatureCatalog catalog, ProducerState state, string value)
    {
        var services = new ServiceCollection();
        var module = new Module(services, catalog, CompositionPolicy.ValidatedV1);
        module.Properties[ProducerState.StatePropertyKey] = state;
        module.Configure<FoundationFeature>(feature => feature.Value = value);
        module.Configure<ConsumerFeature>();
        module.Apply();
        var metadata = services.Single(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance as IInstalledFeatureRegistry;
        Require(metadata?.Find("Fixture.Foundation")?.DisplayName == "Foundation fixture", "Compiled producer metadata must survive package consumption.");
        Require(metadata?.Find("Fixture.Consumer")?.DisplayName == "Consumer fixture", "Consumer metadata must remain registered.");
        return services;
    }

    private static async Task CheckScopesAsync(ServiceProvider provider, string expected)
    {
        Guid firstId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FoundationService>();
            Require(ReferenceEquals(service, scope.ServiceProvider.GetRequiredService<FoundationService>()), "Producer service must be scoped.");
            Require(service.Value == expected, "Runtime feature configuration must reach the producer service.");
            firstId = service.Id;
            await Task.Delay(1);
        }
        await using (var scope = provider.CreateAsyncScope())
            Require(scope.ServiceProvider.GetRequiredService<FoundationService>().Id != firstId, "A new scope must receive a new producer service.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ConsumerFeature(IModule module) : FeatureBase(module)
    {
        public override void Configure() => ((ProducerState)Module.Properties[ProducerState.StatePropertyKey]).Events.Add("consumer.configure");
        public override void Apply() => ((ProducerState)Module.Properties[ProducerState.StatePropertyKey]).Events.Add("consumer.apply");
    }
}
