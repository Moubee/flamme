using Elsa.Features.Abstractions;
using Elsa.Features.Contracts;
using Elsa.Features.Implementations;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.UnitTests;

/// <summary>
/// Characterizes the legacy lifecycle before introducing explicit metadata providers.
/// </summary>
public class LegacyCompositionTests
{
    [Test]
    public async Task Configure_RetainsCustomFactoryInstanceAndRunsEveryConfigurationAction()
    {
        var module = new Module(new ServiceCollection());
        var factoryCalls = 0;
        var first = module.Configure(m =>
        {
            factoryCalls++;
            return new CustomFactoryFeature(m, "configured-value");
        }, f => f.Actions.Add("first"));
        var second = module.Configure<CustomFactoryFeature>(_ => throw new InvalidOperationException("Must reuse the existing feature."),
            f => f.Actions.Add("second"));
        module.Configure<CustomFactoryFeature>(f => f.Actions.Add("third"));
        module.Configure<FactoryDependentFeature>();

        module.Apply();

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(first.Value).IsEqualTo("configured-value");
        await Assert.That(first.Actions.SequenceEqual(["first", "second", "third"])).IsTrue();
        await Assert.That(first.Configurations).IsEqualTo(1);
        await Assert.That(first.Applications).IsEqualTo(1);
        await Assert.That(module.HasFeature<FactoryDependentFeature>()).IsTrue();
    }

    [Test]
    public async Task Apply_InheritsDependencyAndDisplayMetadata()
    {
        var services = new ServiceCollection();
        var module = new Module(services);
        module.Configure<InheritedMetadataFeature>();

        module.Apply();

        var registry = (IInstalledFeatureRegistry)services.Single(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance!;
        var metadata = registry.Find("Elsa.InheritedMetadata")!;
        await Assert.That(module.HasFeature<MetadataDependencyFeature>()).IsTrue();
        await Assert.That(metadata.DisplayName).IsEqualTo("Inherited display name");
        await Assert.That(metadata.Description).IsEqualTo("Inherited description");
        await Assert.That(metadata.Namespace).IsEqualTo("Elsa");
    }

    [Test]
    public async Task Apply_ReappliesExistingInstancesButConfiguresEachInstanceOnce()
    {
        var services = new ServiceCollection();
        var module = new Module(services);
        var first = module.Configure(m => new CustomFactoryFeature(m, "first"));
        module.Apply();

        // Legacy _isApplying remains set after Apply: this feature configures immediately.
        var late = module.Configure<RepeatedApplyFeature>();
        await Assert.That(late.Configurations).IsEqualTo(1);
        await Assert.That(late.Applications).IsEqualTo(0);
        module.Apply();

        await Assert.That(first.Configurations).IsEqualTo(1);
        await Assert.That(first.Applications).IsEqualTo(2);
        await Assert.That(late.Configurations).IsEqualTo(1);
        await Assert.That(late.Applications).IsEqualTo(1);
        await Assert.That(services.Count(x => x.ServiceType == typeof(IInstalledFeatureRegistry))).IsEqualTo(2);
        await Assert.That(services.Count(x => x.ServiceType == typeof(IInstalledFeatureProvider))).IsEqualTo(2);
    }

    [Test]
    public async Task Apply_FiltersConditionalChainInOnePassAndDoesNotCreateTargets()
    {
        var module = new Module(new ServiceCollection());
        var first = module.Configure<FirstConditionalFeature>();
        var second = module.Configure<SecondConditionalFeature>();

        module.Apply();

        // A sees B in the original set; B is then excluded because C is absent.
        await Assert.That(module.HasFeature<FirstConditionalFeature>()).IsTrue();
        await Assert.That(module.HasFeature<SecondConditionalFeature>()).IsFalse();
        await Assert.That(module.HasFeature<MissingConditionalTargetFeature>()).IsFalse();
        await Assert.That(first.Configurations).IsEqualTo(1);
        await Assert.That(second.Configurations).IsEqualTo(1);
        await Assert.That(first.Applications).IsEqualTo(1);
        await Assert.That(second.Applications).IsEqualTo(0);

        // A later Apply performs another single pass, now excluding A as well.
        module.Apply();
        await Assert.That(module.HasFeature<FirstConditionalFeature>()).IsFalse();
        await Assert.That(first.Applications).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_RetainsConditionalFeatureIntroducedAfterInitialFiltering()
    {
        var services = new ServiceCollection();
        var module = new Module(services);
        module.Configure<LateConditionalIntroducingFeature>();

        module.Apply();

        var late = module.Configure<LateConditionalFeature>();
        await Assert.That(module.HasFeature<MissingConditionalTargetFeature>()).IsFalse();
        await Assert.That(module.HasFeature<LateConditionalFeature>()).IsTrue();
        await Assert.That(late.Configurations).IsEqualTo(1);
        await Assert.That(late.Applications).IsEqualTo(1);
        var registry = (IInstalledFeatureRegistry)services.Single(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance!;
        await Assert.That(registry.Find("Elsa.LateConditional")).IsNotNull();
    }

    [Test]
    public async Task Apply_ConfiguresFeaturesAddedFromConfigureAndAppliesTheirDependenciesFirst()
    {
        var module = new Module(new ServiceCollection());
        var events = new List<string>();
        module.Properties["events"] = events;
        module.Configure<ConfigureIntroducingFeature>();

        module.Apply();

        await Assert.That(events.SequenceEqual([
            "root.configure", "dependency.configure", "added.configure",
            "root.apply", "dependency.apply", "added.apply"
        ])).IsTrue();
    }

    [Test]
    public async Task Apply_ReportsMissingDefaultDependencyConstructorWithoutApplyingFeatures()
    {
        var module = new Module(new ServiceCollection());
        var root = module.Configure<UnconstructableDependencyRootFeature>();

        await Assert.That(() => module.Apply()).Throws<MissingMethodException>();

        await Assert.That(root.Applications).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_LegacyCycleRevisitsDependencyMetadataBeforeApplyingFeatures()
    {
        var module = new Module(new ServiceCollection());
        var root = module.Configure<CycleAFeature>();

        await Assert.That(() =>
        {
            try
            {
                module.Apply();
            }
            finally
            {
                _visitedCycleDependency = false;
            }
        }).Throws<CycleDependencyRevisitedException>();

        await Assert.That(root.Applications).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_KeepsStrongDependentWhenItsConditionalDependencyWasFilteredOut()
    {
        var module = new Module(new ServiceCollection());
        var root = module.Configure<StrongConditionalRootFeature>();

        module.Apply();

        await Assert.That(module.HasFeature<StrongConditionalRootFeature>()).IsTrue();
        await Assert.That(module.HasFeature<LateConditionalFeature>()).IsFalse();
        await Assert.That(root.Applications).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_PreservesHostedServiceDescriptorIdentityPriorityTiesAndDirectRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BeforeMarker>();
        var module = new Module(services);
        module.Configure<DescriptorFeature>();

        module.Apply();

        var serviceTypes = services.Select(x => x.ServiceType == typeof(IHostedService) ? x.ImplementationType : x.ServiceType).ToArray();
        await Assert.That(serviceTypes.SequenceEqual([
            typeof(BeforeMarker), typeof(FirstHostedService), typeof(SecondHostedService),
            typeof(SingletonMarker), typeof(IMarker), typeof(ScopedMarker), typeof(TransientMarker),
            typeof(DirectHostedService), typeof(AfterMarker),
            typeof(IInstalledFeatureRegistry), typeof(IInstalledFeatureProvider)
        ])).IsTrue();
        var hosted = services.Where(x => x.ServiceType == typeof(IHostedService)).ToArray();
        await Assert.That(hosted.All(x => x.Lifetime == ServiceLifetime.Singleton && x.ImplementationFactory is null)).IsTrue();
        await Assert.That(hosted.Count(x => x.ImplementationType == typeof(DirectHostedService))).IsEqualTo(1);
        await Assert.That(services.Single(x => x.ServiceType == typeof(IMarker)).ImplementationFactory).IsNotNull();
        await Assert.That(services.Single(x => x.ServiceType == typeof(IMarker)).Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        await Assert.That(services.Single(x => x.ServiceType == typeof(ScopedMarker)).Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(services.Single(x => x.ServiceType == typeof(TransientMarker)).Lifetime).IsEqualTo(ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await Assert.That(provider.GetRequiredService<IMarker>()).IsSameReferenceAs(provider.GetRequiredService<SingletonMarker>());
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var scoped = firstScope.ServiceProvider.GetRequiredService<ScopedMarker>();
        await Assert.That(firstScope.ServiceProvider.GetRequiredService<ScopedMarker>()).IsSameReferenceAs(scoped);
        await Assert.That(ReferenceEquals(scoped, secondScope.ServiceProvider.GetRequiredService<ScopedMarker>())).IsFalse();
        await Assert.That(ReferenceEquals(provider.GetRequiredService<TransientMarker>(), provider.GetRequiredService<TransientMarker>())).IsFalse();
    }

    public abstract class CountingFeature(IModule module) : FeatureBase(module)
    {
        public int Configurations { get; private set; }
        public int Applications { get; private set; }
        public override void Configure() => Configurations++;
        public override void Apply() => Applications++;
    }

    public sealed class CustomFactoryFeature(IModule module, string value) : CountingFeature(module)
    {
        public string Value { get; } = value;
        public List<string> Actions { get; } = [];
    }

    [Elsa.Features.Attributes.DependsOn(typeof(CustomFactoryFeature))]
    public sealed class FactoryDependentFeature(IModule module) : CountingFeature(module);

    [Elsa.Features.Attributes.DependsOn(typeof(MetadataDependencyFeature))]
    [System.ComponentModel.DisplayName("Inherited display name")]
    [System.ComponentModel.Description("Inherited description")]
    public abstract class MetadataBaseFeature(IModule module) : CountingFeature(module);

    public sealed class InheritedMetadataFeature(IModule module) : MetadataBaseFeature(module);
    public sealed class MetadataDependencyFeature(IModule module) : CountingFeature(module);
    public sealed class RepeatedApplyFeature(IModule module) : CountingFeature(module);

    [Elsa.Features.Attributes.DependencyOf(typeof(SecondConditionalFeature))]
    public sealed class FirstConditionalFeature(IModule module) : CountingFeature(module);

    [Elsa.Features.Attributes.DependencyOf(typeof(MissingConditionalTargetFeature))]
    public sealed class SecondConditionalFeature(IModule module) : CountingFeature(module);

    public sealed class MissingConditionalTargetFeature(IModule module) : CountingFeature(module);

    public sealed class LateConditionalIntroducingFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<LateConditionalFeature>();
        }
    }

    [Elsa.Features.Attributes.DependencyOf(typeof(MissingConditionalTargetFeature))]
    public sealed class LateConditionalFeature(IModule module) : CountingFeature(module);

    [Elsa.Features.Attributes.DependsOn(typeof(LateConditionalFeature))]
    public sealed class StrongConditionalRootFeature(IModule module) : CountingFeature(module);

    public sealed class ConfigureIntroducingFeature(IModule module) : FeatureBase(module)
    {
        public override void Configure()
        {
            ((List<string>)Module.Properties["events"]).Add("root.configure");
            Module.Configure<ConfigureAddedFeature>();
        }
        public override void Apply() => ((List<string>)Module.Properties["events"]).Add("root.apply");
    }

    [Elsa.Features.Attributes.DependsOn(typeof(ConfigureDependencyFeature))]
    public sealed class ConfigureAddedFeature(IModule module) : FeatureBase(module)
    {
        public override void Configure() => ((List<string>)Module.Properties["events"]).Add("added.configure");
        public override void Apply() => ((List<string>)Module.Properties["events"]).Add("added.apply");
    }

    public sealed class ConfigureDependencyFeature(IModule module) : FeatureBase(module)
    {
        public override void Configure() => ((List<string>)Module.Properties["events"]).Add("dependency.configure");
        public override void Apply() => ((List<string>)Module.Properties["events"]).Add("dependency.apply");
    }

    [Elsa.Features.Attributes.DependsOn(typeof(CustomFactoryFeature))]
    public sealed class UnconstructableDependencyRootFeature(IModule module) : CountingFeature(module);

    [ThreadStatic]
    private static bool _visitedCycleDependency;

    [CycleDependency]
    public sealed class CycleAFeature(IModule module) : CountingFeature(module);

    private sealed class CycleDependencyAttribute : Elsa.Features.Attributes.DependsOnAttribute
    {
        public CycleDependencyAttribute() : base(typeof(CycleBFeature))
        {
            // Stop the real legacy dependency traversal when it returns to A.
            if (_visitedCycleDependency)
                throw new CycleDependencyRevisitedException();

            _visitedCycleDependency = true;
        }
    }

    private sealed class CycleDependencyRevisitedException : Exception;

    [Elsa.Features.Attributes.DependsOn(typeof(CycleAFeature))]
    public sealed class CycleBFeature(IModule module) : CountingFeature(module);

    public sealed class DescriptorFeature(IModule module) : FeatureBase(module)
    {
        public override void ConfigureHostedServices()
        {
            ConfigureHostedService<FirstHostedService>(3);
            ConfigureHostedService<SecondHostedService>(3);
            ConfigureHostedService<FirstHostedService>(5);
            ConfigureHostedService<DirectHostedService>(-1);
        }

        public override void Apply()
        {
            Services.AddSingleton<SingletonMarker>();
            Services.AddSingleton<IMarker>(sp => sp.GetRequiredService<SingletonMarker>());
            Services.AddScoped<ScopedMarker>();
            Services.AddTransient<TransientMarker>();
            Services.AddHostedService<DirectHostedService>();
            Services.AddSingleton<AfterMarker>();
        }
    }

    public interface IMarker;
    public sealed class BeforeMarker;
    public sealed class SingletonMarker : IMarker;
    public sealed class ScopedMarker;
    public sealed class TransientMarker;
    public sealed class AfterMarker;

    public abstract class HostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    public sealed class FirstHostedService : HostedService;
    public sealed class SecondHostedService : HostedService;
    public sealed class DirectHostedService : HostedService;
}
