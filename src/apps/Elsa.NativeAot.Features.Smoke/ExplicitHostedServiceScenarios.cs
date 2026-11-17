#if EXPLICIT_FEATURE_CATALOG
using Elsa.Features.Abstractions;
using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.NativeAot;

/// <summary>
/// Checks closed hosted-service registrations against the real DI container and lifecycle.
/// </summary>
internal static class ExplicitHostedServiceScenarios
{
    public static async Task RunAsync()
    {
        foreach (var policy in new[] { CompositionPolicy.LegacyV1, CompositionPolicy.ValidatedV1 })
        {
            Console.WriteLine($"CHECK features.explicit-hosted-lifecycle policy={policy}");
            await RunLifecycleAsync(policy);
            Console.WriteLine($"CHECK features.explicit-hosted-admission policy={policy}");
            RunMissingHostedService(policy);
            Console.WriteLine($"CHECK features.explicit-hosted-scopes policy={policy}");
            RunCaptiveScopeValidation(policy);
        }
        Console.WriteLine("CHECK features.explicit-hosted-duplicate-declaration");
        ExpectInvalid(() => new ExplicitFeatureCatalogBuilder()
            .AddHostedService<FirstHostedService>()
            .AddHostedService<FirstHostedService>().Build(), nameof(FirstHostedService));
    }

    private static async Task RunLifecycleAsync(CompositionPolicy policy)
    {
        var catalog = new ExplicitFeatureCatalogBuilder()
            .Add(module => new HostedRootFeature(module), new("HostedRoot", "Probe", "Hosted root"))
            .Add(module => new LateHostedFeature(module), new("LateHosted", "Probe", "Late hosted feature"))
            .AddHostedService<FirstHostedService>()
            .AddHostedService<SecondHostedService>()
            .AddHostedService<LateHostedService>()
            .AddHostedService<DirectHostedService>()
            .Build();
        var expected = new[] { "preexisting", "late", "first", "second", "direct" };

        // The shared catalog must never retain another host's singleton instances or scope state.
        for (var host = 0; host < 2; host++)
        {
            var state = new HostedState();
            var services = CreateServices(state);
            services.AddSingleton<IHostedService, PreExistingHostedService>();
            var module = new Module(services, catalog, policy);
            module.Configure<HostedRootFeature>();
            module.Apply();
            module.Apply();

            var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToArray();
            ProbeRunner.Require(descriptors.Length == expected.Length && descriptors.All(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton),
                "Repeated Apply must retain exactly five singleton hosted-service descriptors.");
            ProbeRunner.Require(descriptors.Select(descriptor => descriptor.ImplementationType).SequenceEqual([
                typeof(PreExistingHostedService), typeof(LateHostedService), typeof(FirstHostedService), typeof(SecondHostedService), typeof(DirectHostedService)
            ]), "Priority ties, duplicate direct registration, and late queued registration must preserve their implementation identities and placement.");
            ProbeRunner.Require(state.Constructors.Count == 0 && state.ScopedCreations == 0,
                "Composition must not instantiate hosted services or their scoped work.");

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            ProbeRunner.Require(state.Constructors.Count == 0 && state.ScopedCreations == 0,
                "Container validation must not execute hosted-service constructors.");
            var hostedServices = provider.GetServices<IHostedService>().ToArray();
            var resolvedAgain = provider.GetServices<IHostedService>().ToArray();
            ProbeRunner.Require(hostedServices.Zip(resolvedAgain).All(pair => ReferenceEquals(pair.First, pair.Second)),
                "Each hosted-service registration must resolve the same singleton instance.");
            ProbeRunner.Require(state.Constructors.SequenceEqual(expected),
                "Hosted-service construction must occur only when resolving the ordered registrations.");
            ProbeRunner.Require(state.ScopedCreations == 0, "A singleton may acquire its work scope only during StartAsync.");

            var started = new List<IHostedService>();
            try
            {
                foreach (var service in hostedServices)
                {
                    await service.StartAsync(CancellationToken.None);
                    started.Add(service);
                }
                ProbeRunner.Require(state.Events.Where(value => value.StartsWith("start:", StringComparison.Ordinal)).SequenceEqual(expected.Select(name => $"start:{name}")),
                    "Real StartAsync calls must complete in registration order.");
                ProbeRunner.Require(state.Active.SetEquals(expected) && state.ScopedCreations == 1 && state.ScopedDisposals == 0,
                    "The owning singleton must create one live scoped dependency during its awaited start.");
            }
            finally
            {
                foreach (var service in started.AsEnumerable().Reverse())
                    await service.StopAsync(CancellationToken.None);
            }

            ProbeRunner.Require(state.Events.Where(value => value.StartsWith("stop:", StringComparison.Ordinal)).SequenceEqual(expected.Reverse().Select(name => $"stop:{name}")),
                "Real StopAsync calls must complete in reverse registration order.");
            ProbeRunner.Require(state.Active.Count == 0 && state.ScopedCreations == 1 && state.ScopedDisposals == 1,
                "Stopping the owning singleton must dispose its own scope exactly once.");
        }
    }

    private static void RunMissingHostedService(CompositionPolicy policy)
    {
        var state = new HostedState();
        var module = new Module(CreateServices(state), new ExplicitFeatureCatalogBuilder().Build(), policy);
        ExpectInvalid(() => module.ConfigureHostedService(typeof(UnknownHostedService)), nameof(UnknownHostedService));
        ExpectInvalid(module.Apply, "fault");
        ProbeRunner.Require(state.Constructors.Count == 0 && state.ScopedCreations == 0,
            "An undeclared hosted-service Type must fail admission before any construction.");
    }

    private static void RunCaptiveScopeValidation(CompositionPolicy policy)
    {
        var state = new HostedState();
        var services = CreateServices(state);
        var catalog = new ExplicitFeatureCatalogBuilder().AddHostedService<CaptiveScopedHostedService>().Build();
        var module = new Module(services, catalog, policy);
        module.ConfigureHostedService<CaptiveScopedHostedService>();
        module.Apply();
        var rejected = false;
        try
        {
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
        catch (AggregateException exception)
        {
            ProbeRunner.Require(exception.Flatten().InnerExceptions.All(inner => inner is InvalidOperationException),
                "The captive-scope failure must come from DI validation.");
            RequireCaptiveDiagnostic(exception);
            rejected = true;
        }
        catch (InvalidOperationException exception)
        {
            RequireCaptiveDiagnostic(exception);
            rejected = true;
        }
        ProbeRunner.Require(rejected, "A singleton hosted service must not capture a scoped constructor dependency.");
        ProbeRunner.Require(state.Constructors.Count == 0 && state.ScopedCreations == 0,
            "Invalid captive scopes must be rejected before running constructors.");
    }

    private static ServiceCollection CreateServices(HostedState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddScoped<ScopedWork>();
        return services;
    }

    private static void RequireCaptiveDiagnostic(Exception exception)
    {
        var diagnostic = exception.ToString();
        foreach (var fragment in new[] { nameof(CaptiveScopedHostedService), nameof(ScopedWork), "scoped", "singleton" })
            ProbeRunner.Require(diagnostic.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"Captive-scope validation must identify '{fragment}': {diagnostic}");
    }

    private static void ExpectInvalid(Action action, params string[] fragments)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            foreach (var fragment in fragments)
                ProbeRunner.Require(exception.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"Expected hosted-service diagnostic containing '{fragment}', received '{exception.Message}'.");
            return;
        }
        throw new InvalidOperationException($"Expected InvalidOperationException identifying {string.Join(", ", fragments)}.");
    }

    private sealed class HostedRootFeature(IModule module) : FeatureBase(module)
    {
        public override void ConfigureHostedServices()
        {
            Module.ConfigureHostedService<FirstHostedService>(5);
            Module.ConfigureHostedService<SecondHostedService>(5);
            Module.ConfigureHostedService<FirstHostedService>(10);
            Module.ConfigureHostedService<DirectHostedService>(-10);
        }

        public override void Apply()
        {
            Services.AddHostedService<DirectHostedService>();
            Module.Configure<LateHostedFeature>();
        }
    }

    private sealed class LateHostedFeature(IModule module) : FeatureBase(module)
    {
        public override void ConfigureHostedServices()
        {
            Module.ConfigureHostedService<LateHostedService>(-1);
            Module.ConfigureHostedService<FirstHostedService>(5);
        }
    }

    private sealed class HostedState
    {
        public List<string> Constructors { get; } = [];
        public List<string> Events { get; } = [];
        public HashSet<string> Active { get; } = [];
        public int ScopedCreations { get; set; }
        public int ScopedDisposals { get; set; }
    }

    private abstract class RecordingHostedService : IHostedService
    {
        protected readonly HostedState State;
        private readonly string _name;
        protected RecordingHostedService(HostedState state, string name)
        {
            State = state;
            _name = name;
            state.Constructors.Add(name);
        }

        public virtual async Task StartAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            State.Events.Add($"start:{_name}");
            ProbeRunner.Require(State.Active.Add(_name), $"Hosted service {_name} started twice.");
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            State.Events.Add($"stop:{_name}");
            ProbeRunner.Require(State.Active.Remove(_name), $"Hosted service {_name} stopped without a matching start.");
        }
    }

    private sealed class PreExistingHostedService : RecordingHostedService
    {
        public PreExistingHostedService(HostedState state) : base(state, "preexisting") { }
    }
    private sealed class SecondHostedService : RecordingHostedService
    {
        public SecondHostedService(HostedState state) : base(state, "second") { }
    }
    private sealed class LateHostedService : RecordingHostedService
    {
        public LateHostedService(HostedState state) : base(state, "late") { }
    }
    private sealed class DirectHostedService : RecordingHostedService
    {
        public DirectHostedService(HostedState state) : base(state, "direct") { }
    }
    private sealed class UnknownHostedService : RecordingHostedService
    {
        public UnknownHostedService(HostedState state) : base(state, "unknown") { }
    }
    private sealed class FirstHostedService : RecordingHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private IServiceScope? _scope;
        public FirstHostedService(HostedState state, IServiceScopeFactory scopeFactory) : base(state, "first") => _scopeFactory = scopeFactory;

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _scope = _scopeFactory.CreateScope();
            _scope.ServiceProvider.GetRequiredService<ScopedWork>();
            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            _scope?.Dispose();
            _scope = null;
        }
    }
    private sealed class ScopedWork : IDisposable
    {
        private readonly HostedState _state;
        public ScopedWork(HostedState state)
        {
            _state = state;
            state.ScopedCreations++;
        }
        public void Dispose() => _state.ScopedDisposals++;
    }
    private sealed class CaptiveScopedHostedService : RecordingHostedService
    {
        public CaptiveScopedHostedService(HostedState state, ScopedWork scopedWork) : base(state, "captive") => _ = scopedWork;
    }
}
#endif