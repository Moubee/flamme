using Elsa.Features.Abstractions;
using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.UnitTests;

/// <summary>
/// Verifies closed hosted-service registration against the module's existing
/// ordering and implementation-type identity contract.
/// </summary>
public class ExplicitHostedServiceTests
{
    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Apply_PreservesPriorityTiesInsertionAndDirectDuplicateIdentity(CompositionPolicy policy)
    {
        var trace = new LifecycleTrace();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddHostedService<PreexistingService>();
        var catalog = new ExplicitFeatureCatalogBuilder()
            .Add(m => new DirectRegistrationFeature(m), Metadata("Direct"))
            .AddHostedService<PreexistingService>()
            .AddHostedService<HighestPriorityService>()
            .AddHostedService<FirstTieService>()
            .AddHostedService<SecondTieService>()
            .AddHostedService<DirectlyRegisteredService>()
            .Build();
        var module = new Module(services, catalog, policy);
        module.ConfigureHostedService<PreexistingService>(-100);
        module.ConfigureHostedService<FirstTieService>(5);
        module.ConfigureHostedService(typeof(SecondTieService), 5);
        module.ConfigureHostedService<HighestPriorityService>(-1);
        module.ConfigureHostedService<DirectlyRegisteredService>(0);
        module.Configure<DirectRegistrationFeature>();

        module.Apply();

        await Assert.That(HostedTypes(services).SequenceEqual(
        [
            typeof(PreexistingService), typeof(HighestPriorityService),
            typeof(FirstTieService), typeof(SecondTieService), typeof(DirectlyRegisteredService)
        ])).IsTrue();
        var markerIndex = services.ToList().FindIndex(x => x.ServiceType == typeof(RegisteredDuringApply));
        var firstTieIndex = services.ToList().FindIndex(x => x.ImplementationType == typeof(FirstTieService));
        var directIndex = services.ToList().FindIndex(x => x.ImplementationType == typeof(DirectlyRegisteredService));
        await Assert.That(firstTieIndex < markerIndex && markerIndex < directIndex).IsTrue();
        await Assert.That(services.Where(x => x.ServiceType == typeof(IHostedService))
            .All(x => x.Lifetime == ServiceLifetime.Singleton && x.ImplementationType is not null && x.ImplementationFactory is null)).IsTrue();
        await Assert.That(trace.Constructors).IsEmpty();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Apply_LateAndRepeatedGenericTypeAndDirectRequestsRemainOneSingletonDescriptor(CompositionPolicy policy)
    {
        var trace = new LifecycleTrace();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        var catalog = new ExplicitFeatureCatalogBuilder()
            .Add(m => new IntroducingFeature(m), Metadata("Introducing"))
            .Add(m => new LateFeature(m), Metadata("Late"))
            .AddHostedService<LateHostedService>()
            .Build();
        var module = new Module(services, catalog, policy);
        module.Configure<IntroducingFeature>();

        module.Apply();
        module.ConfigureHostedService<LateHostedService>(-2);
        module.ConfigureHostedService(typeof(LateHostedService), -2);
        module.Apply();

        await Assert.That(HostedTypes(services).SequenceEqual([typeof(LateHostedService)])).IsTrue();
        var late = module.Configure<LateFeature>();
        await Assert.That(late.Configurations).IsEqualTo(1);
        await Assert.That(late.Applications).IsEqualTo(2);
        await Assert.That(trace.Constructors).IsEmpty();
        await using var provider = services.BuildServiceProvider(ValidatedProviderOptions());
        var first = provider.GetServices<IHostedService>().Single();
        await Assert.That(provider.GetServices<IHostedService>().Single()).IsSameReferenceAs(first);
        await Assert.That(trace.Constructors.SequenceEqual(["late"])).IsTrue();
    }

    [Test]
    [Arguments(false, CompositionPolicy.LegacyV1)]
    [Arguments(false, CompositionPolicy.ValidatedV1)]
    [Arguments(true, CompositionPolicy.LegacyV1)]
    [Arguments(true, CompositionPolicy.ValidatedV1)]
    public async Task ConfigureHostedService_UncoveredTypeFailsBeforeConstructionAndFaultsModule(bool generic, CompositionPolicy policy)
    {
        var trace = new LifecycleTrace();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        var catalog = new ExplicitFeatureCatalogBuilder().AddHostedService<FirstTieService>().Build();
        var module = new Module(services, catalog, policy);

        var failure = CaptureFailure(() =>
        {
            if (generic)
                module.ConfigureHostedService<UncoveredService>();
            else
                module.ConfigureHostedService(typeof(UncoveredService));
        });

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(UncoveredService), StringComparison.Ordinal)).IsTrue();
        await Assert.That(trace.Constructors).IsEmpty();
        await Assert.That(HostedTypes(services)).IsEmpty();
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
        await Assert.That(() => module.ConfigureHostedService<FirstTieService>()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Catalog_RejectsDuplicateHostedServiceDeclarations()
    {
        var failure = CaptureFailure(() => new ExplicitFeatureCatalogBuilder()
            .AddHostedService<FirstTieService>()
            .AddHostedService<FirstTieService>()
            .Build());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(FirstTieService), StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Catalog_SnapshotsHostedServicesBeforeBuilderReuse(CompositionPolicy policy)
    {
        var builder = new ExplicitFeatureCatalogBuilder().AddHostedService<FirstTieService>();
        var catalog = builder.Build();
        builder.AddHostedService<SecondTieService>();
        var module = new Module(new ServiceCollection(), catalog, policy);
        module.ConfigureHostedService<FirstTieService>();

        var failure = CaptureFailure(() => module.ConfigureHostedService<SecondTieService>());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(SecondTieService), StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task ProviderValidation_RejectsSingletonCapturingAScopedConstructorDependency(CompositionPolicy policy)
    {
        var trace = new LifecycleTrace();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddScoped<ScopedDependency>();
        var catalog = new ExplicitFeatureCatalogBuilder().AddHostedService<CaptiveScopedService>().Build();
        var module = new Module(services, catalog, policy);
        module.ConfigureHostedService<CaptiveScopedService>();
        module.Apply();

        var failure = CaptureFailure(() => services.BuildServiceProvider(ValidatedProviderOptions()));

        await Assert.That(failure is AggregateException or InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(ScopedDependency), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(CaptiveScopedService), StringComparison.Ordinal)).IsTrue();
        await Assert.That(trace.Constructors).IsEmpty();
        await Assert.That(trace.ScopeIds).IsEmpty();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Services_ConstructOnceStartInDescriptorOrderAndStopInReverseWithOwnedScopes(CompositionPolicy policy)
    {
        var catalog = LifecycleCatalog();
        var trace = new LifecycleTrace();
        var services = CreateLifecycleServices(catalog, trace, policy);
        await Assert.That(trace.Constructors).IsEmpty();
        await using var provider = services.BuildServiceProvider(ValidatedProviderOptions());
        var hosted = provider.GetServices<IHostedService>().ToArray();
        await Assert.That(trace.Constructors.SequenceEqual(["scoped", "second"])).IsTrue();
        var resolvedAgain = provider.GetServices<IHostedService>().ToArray();
        await Assert.That(resolvedAgain[0]).IsSameReferenceAs(hosted[0]);
        await Assert.That(resolvedAgain[1]).IsSameReferenceAs(hosted[1]);

        // Drive the standard hosted-service sequence using the module's ordered
        // descriptors; no worker/background implementation is substituted.
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
        foreach (var service in hosted.Reverse())
            await service.StopAsync(CancellationToken.None);

        await Assert.That(trace.Starts.SequenceEqual(["scoped", "second"])).IsTrue();
        await Assert.That(trace.Stops.SequenceEqual(["second", "scoped"])).IsTrue();
        await Assert.That(trace.ScopeIds.Count).IsEqualTo(1);
        await Assert.That(trace.DisposedScopeIds.SequenceEqual(trace.ScopeIds)).IsTrue();
        await Assert.That(trace.SameScopedInstance).IsTrue();
        await Assert.That(trace.Constructors.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task ReusedCatalog_KeepsTwoHostsSingletonsAndScopedDependenciesIsolated(CompositionPolicy policy)
    {
        var catalog = LifecycleCatalog();
        var firstTrace = new LifecycleTrace();
        var secondTrace = new LifecycleTrace();
        await using var first = CreateLifecycleServices(catalog, firstTrace, policy).BuildServiceProvider(ValidatedProviderOptions());
        await using var second = CreateLifecycleServices(catalog, secondTrace, policy).BuildServiceProvider(ValidatedProviderOptions());
        var firstHosted = first.GetServices<IHostedService>().ToArray();
        var secondHosted = second.GetServices<IHostedService>().ToArray();

        await Assert.That(ReferenceEquals(firstHosted[0], secondHosted[0])).IsFalse();
        await Assert.That(ReferenceEquals(firstHosted[1], secondHosted[1])).IsFalse();
        foreach (var service in firstHosted)
            await service.StartAsync(CancellationToken.None);
        await Assert.That(secondTrace.Starts).IsEmpty();
        foreach (var service in secondHosted)
            await service.StartAsync(CancellationToken.None);
        foreach (var service in firstHosted.Reverse())
            await service.StopAsync(CancellationToken.None);
        await Assert.That(secondTrace.Stops).IsEmpty();
        foreach (var service in secondHosted.Reverse())
            await service.StopAsync(CancellationToken.None);

        await Assert.That(firstTrace.ScopeIds.Count).IsEqualTo(1);
        await Assert.That(secondTrace.ScopeIds.Count).IsEqualTo(1);
        await Assert.That(firstTrace.ScopeIds.Single() != secondTrace.ScopeIds.Single()).IsTrue();
        await Assert.That(firstTrace.Starts.SequenceEqual(["scoped", "second"])).IsTrue();
        await Assert.That(secondTrace.Stops.SequenceEqual(["second", "scoped"])).IsTrue();
    }

    private static ExplicitFeatureCatalog LifecycleCatalog() => new ExplicitFeatureCatalogBuilder()
        .AddHostedService<ScopedStartingService>()
        .AddHostedService<SecondTieService>()
        .Build();

    private static ServiceCollection CreateLifecycleServices(ExplicitFeatureCatalog catalog, LifecycleTrace trace, CompositionPolicy policy)
    {
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddScoped<ScopedDependency>();
        var module = new Module(services, catalog, policy);
        module.ConfigureHostedService<SecondTieService>(10);
        module.ConfigureHostedService<ScopedStartingService>(-1);
        module.Apply();
        return services;
    }

    private static ServiceProviderOptions ValidatedProviderOptions() => new() { ValidateOnBuild = true, ValidateScopes = true };
    private static List<Type> HostedTypes(IServiceCollection services) => services
        .Where(x => x.ServiceType == typeof(IHostedService)).Select(x => x.ImplementationType!).ToList();
    private static FeatureDescriptor Metadata(string name) => new(name, "Tests", name);
    private static Exception CaptureFailure(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected an operation to fail.");
    }

    public sealed class LifecycleTrace
    {
        public List<string> Constructors { get; } = [];
        public List<string> Starts { get; } = [];
        public List<string> Stops { get; } = [];
        public List<Guid> ScopeIds { get; } = [];
        public List<Guid> DisposedScopeIds { get; } = [];
        public bool SameScopedInstance { get; set; }
    }

    public abstract class RecordingService : IHostedService
    {
        protected RecordingService(LifecycleTrace trace, string name)
        {
            Trace = trace;
            Name = name;
            trace.Constructors.Add(name);
        }
        protected LifecycleTrace Trace { get; }
        private string Name { get; }
        public virtual Task StartAsync(CancellationToken cancellationToken) { Trace.Starts.Add(Name); return Task.CompletedTask; }
        public virtual Task StopAsync(CancellationToken cancellationToken) { Trace.Stops.Add(Name); return Task.CompletedTask; }
    }

    public sealed class PreexistingService(LifecycleTrace trace) : RecordingService(trace, "preexisting");
    public sealed class HighestPriorityService(LifecycleTrace trace) : RecordingService(trace, "highest");
    public sealed class FirstTieService(LifecycleTrace trace) : RecordingService(trace, "first");
    public sealed class SecondTieService(LifecycleTrace trace) : RecordingService(trace, "second");
    public sealed class DirectlyRegisteredService(LifecycleTrace trace) : RecordingService(trace, "direct");
    public sealed class LateHostedService(LifecycleTrace trace) : RecordingService(trace, "late");
    public sealed class UncoveredService(LifecycleTrace trace) : RecordingService(trace, "uncovered");
    public sealed class RegisteredDuringApply;

    public sealed class DirectRegistrationFeature(IModule module) : FeatureBase(module)
    {
        public override void Apply()
        {
            Services.AddSingleton<RegisteredDuringApply>();
            Services.AddHostedService<DirectlyRegisteredService>();
        }
    }

    public sealed class IntroducingFeature(IModule module) : FeatureBase(module)
    {
        public override void Apply() => Module.Configure<LateFeature>();
    }

    public sealed class LateFeature(IModule module) : FeatureBase(module)
    {
        public int Configurations { get; private set; }
        public int Applications { get; private set; }
        public override void ConfigureHostedServices()
        {
            Configurations++;
            Module.ConfigureHostedService<LateHostedService>(-2);
            Module.ConfigureHostedService(typeof(LateHostedService), -2);
        }
        public override void Apply()
        {
            Applications++;
            Services.AddHostedService<LateHostedService>();
        }
    }

    public sealed class ScopedDependency : IDisposable
    {
        private readonly LifecycleTrace _trace;
        public ScopedDependency(LifecycleTrace trace) { _trace = trace; trace.ScopeIds.Add(Id); }
        public Guid Id { get; } = Guid.NewGuid();
        public void Dispose() => _trace.DisposedScopeIds.Add(Id);
    }

    public sealed class CaptiveScopedService(ScopedDependency dependency, LifecycleTrace trace) : RecordingService(trace, "captive")
    {
        public ScopedDependency Dependency { get; } = dependency;
    }

    public sealed class ScopedStartingService(IServiceScopeFactory scopeFactory, LifecycleTrace trace) : RecordingService(trace, "scoped")
    {
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dependency = scope.ServiceProvider.GetRequiredService<ScopedDependency>();
            Trace.SameScopedInstance = ReferenceEquals(dependency, scope.ServiceProvider.GetRequiredService<ScopedDependency>());
            await Task.Yield();
            await base.StartAsync(cancellationToken);
        }
    }
}