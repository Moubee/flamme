using Elsa.Features.Abstractions;
using Elsa.Features.Contracts;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.UnitTests;

public class FixedFeatureModuleTests
{
    [Test]
    public async Task HostFactoryConfiguresSelectedInstanceBeforeLifecycleAndRepeatedApplyReusesIt()
    {
        var module = new TestPlan(new ServiceCollection());
        var factoryCalls = 0;
        module.ConfigureHost(host => host.Configure(m =>
        {
            factoryCalls++;
            return new ChildFeature(m, "custom");
        }, child => child.Events.Add("first")));
        module.ConfigureHost(host => host.Configure<ChildFeature>(child => child.Events.Add("second")));
        await Assert.That(module.HasFeature<ChildFeature>()).IsTrue();
        await Assert.That(module.HasFeature<UnplannedFeature>()).IsFalse();

        module.Apply();
        module.ConfigureHost(_ => throw new InvalidOperationException("Host configuration must only run on the first Apply."));
        module.Apply();

        var child = module.Configure<ChildFeature>(_ => throw new InvalidOperationException("Already constructed."));
        await Assert.That(child.Value).IsEqualTo("custom");
        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(string.Join(",", child.Events)).IsEqualTo("first,second,base.configure,child.configure,base.apply,child.apply,base.apply,child.apply");
        var hosted = module.Services.Where(item => item.ServiceType == typeof(IHostedService)).ToArray();
        await Assert.That(hosted.Select(item => item.ImplementationType).SequenceEqual([typeof(LowService), typeof(HighService)])).IsTrue();
        await Assert.That(module.Services.IndexOf(hosted[1]) < module.Services.ToList().FindIndex(item => item.ServiceType == typeof(string))).IsTrue();
        using var provider = module.Services.BuildServiceProvider();
        var descriptors = provider.GetRequiredService<IInstalledFeatureRegistry>().List();
        await Assert.That(descriptors.Select(item => item.FullName).SequenceEqual(["Test.Base", "Test.Child"])).IsTrue();

        var other = new TestPlan(new ServiceCollection());
        await Assert.That(other.Configure<ChildFeature>().Value).IsEqualTo("default");
        await Assert.That(ReferenceEquals(other.Configure<ChildFeature>(), child)).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task QueuedHostedServicesFollowSurvivingRegistrationsAndPrecedeThoseAddedDuringApply(bool removeAll)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        services.AddSingleton(new Uri("about:blank"));
        var module = new TestPlan(services);
        module.Configure<ChildFeature>(child => child.WhenApplied = () =>
        {
            services.RemoveAt(0);
            if (removeAll)
                services.RemoveAt(0);
        });

        module.Apply();

        var order = services.Select(descriptor => descriptor.ImplementationType ?? descriptor.ServiceType).ToArray();
        var firstHosted = removeAll ? 0 : 1;
        if (!removeAll)
            await Assert.That(order[0]).IsEqualTo(typeof(Uri));
        await Assert.That(order.Skip(firstHosted).Take(3).SequenceEqual([typeof(LowService), typeof(HighService), typeof(string)])).IsTrue();
    }

    [Test]
    public async Task InPlaceServiceReplacementStaysAheadOfQueuedHostedServices()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddHostedService<HostService>();
        var replacement = ServiceDescriptor.Singleton<IHostedService>(_ => new HostService());
        var module = new TestPlan(services);
        module.Configure<ChildFeature>(child => child.WhenApplied = () => services[0] = replacement);
        module.Apply();

        await Assert.That(services[0]).IsSameReferenceAs(replacement);
        await Assert.That(services[1].ImplementationType).IsEqualTo(typeof(LowService));
    }

    [Test]
    public async Task LaterHostedRequestsHonorPriorityAndPreserveHostOwnedDuplicates()
    {
        var services = new ServiceCollection();
        services.AddHostedService<LowService>();
        var original = services[0];
        var module = new TestPlan(services);
        module.Apply();
        services.AddHostedService<HostService>();
        module.ConfigureHostedService<LateService>(0);
        module.Apply();
        module.Apply();

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToArray();
        await Assert.That(hosted[0]).IsSameReferenceAs(original);
        await Assert.That(hosted.Select(d => d.ImplementationType).SequenceEqual(
            [typeof(LowService), typeof(LateService), typeof(HighService), typeof(HostService)])).IsTrue();
    }

    [Test]
    public async Task SelectedFeatureWithoutDefaultConstructorAcceptsHostFactory()
    {
        var module = new TestPlan(new ServiceCollection(), hasDefaultFactory: false);
        module.ConfigureHost(host => host.Configure(m => new ChildFeature(m, "host")));
        module.Apply();
        await Assert.That(module.Configure<ChildFeature>().Value).IsEqualTo("host");

        var missing = new TestPlan(new ServiceCollection(), hasDefaultFactory: false);
        await Assert.That(missing.Apply).Throws<MissingMethodException>().WithMessageContaining(nameof(ChildFeature));
    }

    [Test]
    public async Task UnselectedRequestDuringApplyCannotRunItsFactoryOrBeSwallowed()
    {
        var module = new TestPlan(new ServiceCollection());
        var ranFactory = false;
        module.Configure<ChildFeature>(child => child.WhenApplied = () =>
        {
            try
            {
                module.Configure(m =>
                {
                    ranFactory = true;
                    return new UnplannedFeature(m);
                });
            }
            catch (InvalidOperationException) { }
        });

        await Assert.That(module.Apply).Throws<InvalidOperationException>().WithMessageContaining("faulted");
        await Assert.That(ranFactory).IsFalse();
        await Assert.That(module.HasFeature<UnplannedFeature>()).IsFalse();
        await Assert.That(module.Apply).Throws<InvalidOperationException>().WithMessageContaining("faulted");
        await Assert.That(module.Services.Any(item => item.ServiceType == typeof(IInstalledFeatureRegistry))).IsFalse();
    }

    [Test]
    public async Task SealedModuleRejectsAdditionalSlotsAndHostedServiceBindings()
    {
        var module = new TestPlan(new ServiceCollection());
        await Assert.That(() => module.Declare<UnplannedFeature>(m => new(m), new("Extra", "Test", "Extra"))).Throws<InvalidOperationException>();
        await Assert.That(() => module.DeclareHostedService<LowService>()).Throws<InvalidOperationException>();
        await Assert.That(module.HasFeature<UnplannedFeature>()).IsFalse();
    }

    private sealed class TestPlan : FixedFeatureModule
    {
        private readonly FixedFeatureSlot _base;
        private readonly FixedFeatureSlot _child;

        public TestPlan(IServiceCollection services, bool hasDefaultFactory = true) : base(services)
        {
            _base = Declare<BaseFeature>(m => new(m), new("Base", "Test", "Base"));
            _child = Declare<ChildFeature>(hasDefaultFactory ? m => new(m, "default") : null, new("Child", "Test", "Child"));
            DeclareHostedService<LowService>();
            DeclareHostedService<HighService>();
            DeclareHostedService<LateService>();
            SealDeclarations();
        }

        protected override void ConfigureFeatures()
        {
            _base.Configure();
            _child.Configure();
        }

        protected override void ApplyFeatures()
        {
            _base.Apply();
            _child.Apply();
        }
    }

    private sealed class BaseFeature(IModule module) : FeatureBase(module)
    {
        public override void Configure() => Module.Configure<ChildFeature>(child => child.Events.Add("base.configure"));
        public override void ConfigureHostedServices() => Module.ConfigureHostedService<HighService>(10);
        public override void Apply() => Module.Configure<ChildFeature>(child => child.Events.Add("base.apply"));
    }

    private sealed class ChildFeature(IModule module, string value) : FeatureBase(module)
    {
        public string Value => value;
        public List<string> Events { get; } = [];
        public Action? WhenApplied { get; set; }
        public override void Configure() => Events.Add("child.configure");
        public override void ConfigureHostedServices() => Module.ConfigureHostedService<LowService>(1);

        public override void Apply()
        {
            WhenApplied?.Invoke();
            Events.Add("child.apply");
            Services.AddSingleton(Value);
        }
    }

    private sealed class UnplannedFeature(IModule module) : FeatureBase(module);

    private class LowService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HighService : LowService;
    private sealed class LateService : LowService;
    private sealed class HostService : LowService;
}
