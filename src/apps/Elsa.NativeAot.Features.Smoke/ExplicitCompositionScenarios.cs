using Elsa.Features.Abstractions;
using Elsa.Features.Contracts;
using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.NativeAot;

/// <summary>
/// Exercises the production catalog lifecycle without rooting its reflection provider.
/// </summary>
internal static class ExplicitCompositionScenarios
{
    public static Task RunAsync()
    {
        foreach (var policy in new[] { CompositionPolicy.LegacyV1, CompositionPolicy.ValidatedV1 })
        {
            Console.WriteLine($"CHECK features.explicit-graph policy={policy}");
            RunGraph(policy);
            Console.WriteLine($"CHECK features.explicit-conditionals policy={policy}");
            RunConditionals(policy);
        }
        Console.WriteLine("CHECK features.explicit-invalid-catalogs");
        RunInvalidCatalogs();
        Console.WriteLine("CHECK features.explicit-admission-and-faults");
        RunAdmissionFailures();
        return Task.CompletedTask;
    }

    private static void RunGraph(CompositionPolicy policy)
    {
        var rootMetadata = Metadata("Root", "Manifest root", "Manifest description");
        var leftDependencies = new[] { typeof(SharedFeature) };
        var builder = new ExplicitFeatureCatalogBuilder()
            .Add(m => new RootFeature(m), rootMetadata, [typeof(LeftFeature), typeof(RightFeature), typeof(FactoryFeature)])
            .Add(m => new LeftFeature(m), Metadata("Left"), leftDependencies)
            .Add(m => new RightFeature(m), Metadata("Right"), [typeof(SharedFeature)])
            .Add(m => new SharedFeature(m), Metadata("Shared"))
            .Add(m => new FactoryFeature(m, "catalog"), Metadata("Factory"))
            .Add(m => new LateFirstFeature(m), Metadata("LateFirst"))
            .Add(m => new LateSecondFeature(m), Metadata("LateSecond"), [typeof(SharedFeature)]);
        var catalog = builder.Build();
        rootMetadata.DisplayName = "caller mutation";
        leftDependencies[0] = typeof(UnknownFeature);

        // The same immutable catalog may be consumed independently by multiple hosts.
        for (var host = 0; host < 2; host++)
        {
            var services = new ServiceCollection();
            var state = new ScenarioState();
            var module = new Module(services, catalog, policy);
            module.Properties["state"] = state;
            var factoryCalls = 0;
            var custom = module.Configure<FactoryFeature>(m =>
            {
                factoryCalls++;
                return new FactoryFeature(m, "runtime override");
            }, f => f.Actions.Add("first"));
            var repeated = module.Configure<FactoryFeature>(_ => throw new InvalidOperationException("Existing instance must be retained."),
                f => f.Actions.Add("second"));
            module.Configure<RootFeature>();
            module.Apply();

            ScenarioAssert.Require(ReferenceEquals(custom, repeated) && factoryCalls == 1 && custom.Value == "runtime override",
                "Catalog metadata must preserve the runtime factory override and instance.");
            ScenarioAssert.Require(custom.Actions.SequenceEqual(["first", "second"]), "Every Configure action must run once in call order.");
            RequireCounts(state, configurations: 1, applications: 1);
            Before(state, "shared.apply", "left.apply");
            Before(state, "shared.apply", "right.apply");
            Before(state, "left.apply", "root.apply");
            Before(state, "right.apply", "root.apply");
            Before(state, "factory.apply", "root.apply");
            Before(state, "root.apply", "late-first.apply");
            Before(state, "late-first.apply", "late-second.apply");
            var registry = (IInstalledFeatureRegistry)services.Last(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance!;
            var metadata = registry.Find("Probe.Root");
            ScenarioAssert.Require(metadata is { DisplayName: "Manifest root", Description: "Manifest description" },
                "Installed feature metadata must come from the catalog snapshot.");
            metadata!.DisplayName = "host registry mutation";

            module.Configure<FactoryFeature>(f => f.Actions.Add("third"));
            module.Apply();
            RequireCounts(state, configurations: 1, applications: 2);
            ScenarioAssert.Require(custom.Actions.SequenceEqual(["first", "second", "third"]), "Repeated Apply must retain runtime configuration values.");
        }
    }

    private static void RunConditionals(CompositionPolicy policy)
    {
        var catalog = ConditionalBuilder().Build();
        var state = new ScenarioState();
        var module = CreateModule(catalog, policy, state);
        module.Configure<ConditionalAFeature>();
        module.Configure<ConditionalBFeature>();
        module.Apply();
        ScenarioAssert.Require(module.HasFeature<ConditionalAFeature>() == (policy == CompositionPolicy.LegacyV1),
            $"The selected {policy} must control single-pass versus fixed-point filtering.");
        ScenarioAssert.Require(!module.HasFeature<ConditionalBFeature>() && !module.HasFeature<ConditionalCFeature>(),
            "Conditional presence must never install its absent target.");
        ScenarioAssert.Require(state.Events.Count(x => x == "conditional-a.configure") == 1 && state.Events.Count(x => x == "conditional-b.configure") == 1,
            "Legacy and validated policies configure candidates before conditional filtering.");
        ScenarioAssert.Require(!state.Events.Any(x => x.StartsWith("conditional-c.", StringComparison.Ordinal)),
            "An unselected conditional target must not be configured or applied.");
        module.Apply();
        ScenarioAssert.Require(!module.HasFeature<ConditionalAFeature>(), "Repeated legacy filtering must remove A after B disappears.");

        if (policy == CompositionPolicy.LegacyV1)
        {
            var legacy = CreateModule(ConditionalBuilder().Add(m => new StrongRootFeature(m), Metadata("StrongRoot"), [typeof(ConditionalBFeature)]).Build(), policy, new());
            legacy.Configure<StrongRootFeature>();
            legacy.Apply();
            ScenarioAssert.Require(legacy.HasFeature<StrongRootFeature>() && !legacy.HasFeature<ConditionalBFeature>(),
                "Legacy policy must retain its characterized strong-dependent behavior.");
        }
    }

    private static void RunInvalidCatalogs()
    {
        ExpectInvalid(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new RootFeature(m), Metadata("Root"), [typeof(UnknownFeature)]).Build(), nameof(RootFeature), nameof(UnknownFeature));
        ExpectInvalid(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new RootFeature(m), Metadata("Root"))
            .Add(m => new RootFeature(m), Metadata("OtherRoot")).Build(), nameof(RootFeature));
        ExpectInvalid(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new LeftFeature(m), Metadata("Duplicate"))
            .Add(m => new RightFeature(m), Metadata("Duplicate")).Build(), "Probe.Duplicate");
        ExpectInvalid(() => new ExplicitFeatureCatalogBuilder()
            .Add(typeof(OpenFeature<>), m => new UnknownFeature(m), Metadata("Open")).Build(), "OpenFeature", "closed");
        ExpectInvalid(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new CycleAFeature(m), Metadata("CycleA"), [typeof(CycleBFeature)])
            .Add(m => new CycleBFeature(m), Metadata("CycleB"), [typeof(CycleAFeature)]).Build(), nameof(CycleAFeature), nameof(CycleBFeature), "cycl");
    }

    private static void RunAdmissionFailures()
    {
        var unknown = CreateModule(new ExplicitFeatureCatalogBuilder().Build(), CompositionPolicy.ValidatedV1, new());
        ExpectInvalid(() => unknown.Configure<UnknownFeature>(), nameof(UnknownFeature));
        ExpectInvalid(unknown.Apply, "fault");

        var wrong = CreateModule(new ExplicitFeatureCatalogBuilder()
            .Add(m => new FactoryIdentityFeature(m), Metadata("FactoryIdentity")).Build(), CompositionPolicy.ValidatedV1, new());
        ExpectInvalid(() => wrong.Configure<FactoryIdentityFeature>(m => new DifferentFactoryFeature(m)), nameof(FactoryIdentityFeature), nameof(DifferentFactoryFeature));
        ExpectInvalid(wrong.Apply, "fault");

        var nullFactory = CreateModule(new ExplicitFeatureCatalogBuilder()
            .Add(m => new FactoryIdentityFeature(m), Metadata("FactoryIdentity")).Build(), CompositionPolicy.ValidatedV1, new());
        ExpectInvalid(() => nullFactory.Configure<FactoryIdentityFeature>(factory: _ => null!), nameof(FactoryIdentityFeature), "null");
        ExpectInvalid(nullFactory.Apply, "fault");

        var callback = CreateModule(new ExplicitFeatureCatalogBuilder()
            .Add(m => new FactoryIdentityFeature(m), Metadata("FactoryIdentity")).Build(), CompositionPolicy.ValidatedV1, new());
        ExpectInvalid(() => callback.Configure<FactoryIdentityFeature>(configure: _ => throw new InvalidOperationException("configuration-callback-failed")), "configuration-callback-failed");
        ExpectInvalid(callback.Apply, "fault");

        var strongState = new ScenarioState();
        var strong = CreateModule(ConditionalBuilder()
            .Add(m => new StrongRootFeature(m), Metadata("StrongRoot"), [typeof(ConditionalBFeature)]).Build(), CompositionPolicy.ValidatedV1, strongState);
        strong.Configure<StrongRootFeature>();
        ExpectInvalid(strong.Apply, nameof(StrongRootFeature), nameof(ConditionalBFeature));
        ScenarioAssert.Require(!strongState.Events.Any(x => x.EndsWith(".apply", StringComparison.Ordinal)),
            "Invalid initial admission must reject the whole batch before Apply callbacks.");
        ExpectInvalid(strong.Apply, "fault");

        var lateState = new ScenarioState();
        var late = CreateModule(ConditionalBuilder()
            .Add(m => new InvalidIntroducingFeature(m), Metadata("InvalidIntroducing"))
            .Add(m => new StrongRootFeature(m), Metadata("StrongRoot"), [typeof(ConditionalBFeature)]).Build(), CompositionPolicy.ValidatedV1, lateState);
        late.Configure<InvalidIntroducingFeature>();
        ExpectInvalid(late.Apply, nameof(StrongRootFeature), nameof(ConditionalBFeature));
        ScenarioAssert.Require(!lateState.Events.Contains("strong-root.apply") && !lateState.Events.Contains("conditional-b.apply"),
            "An invalid late dependency must never be applied after its admission fails.");
        ExpectInvalid(late.Apply, "fault");
    }

    private static ExplicitFeatureCatalogBuilder ConditionalBuilder() => new ExplicitFeatureCatalogBuilder()
        .Add(m => new ConditionalAFeature(m), Metadata("ConditionalA"), dependencyOf: [typeof(ConditionalBFeature)])
        .Add(m => new ConditionalBFeature(m), Metadata("ConditionalB"), dependencyOf: [typeof(ConditionalCFeature)])
        .Add(m => new ConditionalCFeature(m), Metadata("ConditionalC"));

    private static Module CreateModule(ExplicitFeatureCatalog catalog, CompositionPolicy policy, ScenarioState state)
    {
        var module = new Module(new ServiceCollection(), catalog, policy);
        module.Properties["state"] = state;
        return module;
    }

    private static FeatureDescriptor Metadata(string name, string? displayName = null, string? description = null) => new(name, "Probe", displayName ?? name, description);

    private static void RequireCounts(ScenarioState state, int configurations, int applications)
    {
        foreach (var name in new[] { "root", "left", "right", "shared", "factory", "late-first", "late-second" })
        {
            ScenarioAssert.Require(state.Events.Count(x => x == $"{name}.configure") == configurations && state.Events.Count(x => x == $"{name}.hosted") == configurations,
                $"{name} must configure its lifecycle exactly {configurations} time(s).");
            ScenarioAssert.Require(state.Events.Count(x => x == $"{name}.apply") == applications,
                $"{name} must apply exactly {applications} time(s), including repeated Apply.");
        }
    }

    private static void Before(ScenarioState state, string first, string second) => ScenarioAssert.Require(
        state.Events.IndexOf(first) >= 0 && state.Events.IndexOf(first) < state.Events.IndexOf(second), $"Expected {first} before {second}.");

    private static void ExpectInvalid(Action action, params string[] fragments)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            foreach (var fragment in fragments)
                ScenarioAssert.Require(exception.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"Expected diagnostic containing '{fragment}', received '{exception.Message}'.");
            return;
        }
        throw new InvalidOperationException($"Expected InvalidOperationException identifying {string.Join(", ", fragments)}.");
    }

    private sealed class ScenarioState
    {
        public List<string> Events { get; } = [];
    }

    private abstract class RecordingFeature(IModule module, string name) : FeatureBase(module)
    {
        public override void Configure() => ((ScenarioState)Module.Properties["state"]).Events.Add($"{name}.configure");
        public override void ConfigureHostedServices() => ((ScenarioState)Module.Properties["state"]).Events.Add($"{name}.hosted");
        public override void Apply() => ((ScenarioState)Module.Properties["state"]).Events.Add($"{name}.apply");
    }

    private sealed class RootFeature(IModule module) : RecordingFeature(module, "root")
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<LateFirstFeature>();
        }
    }
    private sealed class LeftFeature(IModule module) : RecordingFeature(module, "left");
    private sealed class RightFeature(IModule module) : RecordingFeature(module, "right");
    private sealed class SharedFeature(IModule module) : RecordingFeature(module, "shared");
    private sealed class FactoryFeature(IModule module, string value) : RecordingFeature(module, "factory")
    {
        public string Value { get; } = value;
        public List<string> Actions { get; } = [];
    }
    private sealed class LateFirstFeature(IModule module) : RecordingFeature(module, "late-first")
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<LateSecondFeature>();
        }
    }
    private sealed class LateSecondFeature(IModule module) : RecordingFeature(module, "late-second");
    private sealed class ConditionalAFeature(IModule module) : RecordingFeature(module, "conditional-a");
    private sealed class ConditionalBFeature(IModule module) : RecordingFeature(module, "conditional-b");
    private sealed class ConditionalCFeature(IModule module) : RecordingFeature(module, "conditional-c");
    private sealed class StrongRootFeature(IModule module) : RecordingFeature(module, "strong-root");
    private sealed class InvalidIntroducingFeature(IModule module) : RecordingFeature(module, "invalid-introducing")
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<StrongRootFeature>();
        }
    }
    private sealed class UnknownFeature(IModule module) : RecordingFeature(module, "unknown");
    private class FactoryIdentityFeature(IModule module) : RecordingFeature(module, "factory-identity");
    private sealed class DifferentFactoryFeature(IModule module) : FactoryIdentityFeature(module);
    private sealed class OpenFeature<T>(IModule module) : RecordingFeature(module, "open");
    private sealed class CycleAFeature(IModule module) : RecordingFeature(module, "cycle-a");
    private sealed class CycleBFeature(IModule module) : RecordingFeature(module, "cycle-b");
}
