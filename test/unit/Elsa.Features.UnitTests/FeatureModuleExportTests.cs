using Elsa.CodeGeneration;
using Elsa.Features.Abstractions;
using Elsa.Features.Contracts;
using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Features.UnitTests;

/// <summary>
/// Verifies versioned export composition without executing producer factories or
/// changing the module's runtime configuration and lifecycle semantics.
/// </summary>
public class FeatureModuleExportTests
{
    [Test]
    public async Task Compose_ResolvesATrueCrossExportStrongDependencyBeforeRuntimeActivation()
    {
        var factoryCalls = new List<string>();
        var consumerBuilder = new ExplicitFeatureCatalogBuilder()
            .Add(m =>
            {
                factoryCalls.Add("root");
                return new RootFeature(m);
            }, Metadata("Root"), [typeof(LeafFeature)]);
        await AssertFailureAsync(() => consumerBuilder.Build(), nameof(RootFeature), nameof(LeafFeature));
        var consumer = consumerBuilder.CreateExport(new("consumer", 1, [new ModuleRequirement("provider", 1)]));
        var provider = Export("provider", m =>
        {
            factoryCalls.Add("leaf");
            return new LeafFeature(m);
        }, "Leaf");

        var catalog = new FeatureModuleCatalogBuilder().Add(consumer).Add(provider).Build();

        await Assert.That(factoryCalls).IsEmpty();
        await Assert.That(catalog.FeatureTypes.SequenceEqual([typeof(LeafFeature), typeof(RootFeature)])).IsTrue();
        var module = new Module(new ServiceCollection(), catalog, CompositionPolicy.ValidatedV1);
        var events = AttachEvents(module);
        var root = module.Configure<RootFeature>();
        await Assert.That(factoryCalls.SequenceEqual(["root"])).IsTrue();
        await Assert.That(events).IsEmpty();
        module.Apply();
        await Assert.That(factoryCalls.SequenceEqual(["root", "leaf"])).IsTrue();
        await Assert.That(events.SequenceEqual(["leaf.configure", "root.configure", "leaf.apply", "root.apply"])).IsTrue();
        await Assert.That(root.Applications).IsEqualTo(1);
    }

    [Test]
    public async Task Compose_RequiresTheDependencyOwnerInTheDeclaredModuleRequirementClosure()
    {
        var calls = 0;
        var consumer = Export("consumer", m => { calls++; return new RootFeature(m); }, "Root", [typeof(LeafFeature)]);
        var provider = Export("provider", m => { calls++; return new LeafFeature(m); }, "Leaf");

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(consumer).Add(provider).Build(),
            "consumer", "provider", nameof(RootFeature), nameof(LeafFeature));
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task Compose_AdmitsTransitiveRequirementsWithoutAutomaticallyInstallingUnrelatedFeatures()
    {
        var consumer = Export("consumer", m => new RootFeature(m), "Root", [typeof(LeafFeature)],
            requirements: [new ModuleRequirement("middle", 1)]);
        var middle = Export("middle", m => new MiddleFeature(m), "Middle",
            requirements: [new ModuleRequirement("provider", 1)]);
        var provider = Export("provider", m => new LeafFeature(m), "Leaf");

        var catalog = new FeatureModuleCatalogBuilder().Add(consumer).Add(provider).Add(middle).Build();

        await Assert.That(catalog.FeatureTypes.SequenceEqual([typeof(LeafFeature), typeof(MiddleFeature), typeof(RootFeature)])).IsTrue();
        var module = new Module(new ServiceCollection(), catalog, CompositionPolicy.ValidatedV1);
        AttachEvents(module);
        module.Configure<RootFeature>();
        module.Apply();
        await Assert.That(module.HasFeature<LeafFeature>()).IsTrue();
        await Assert.That(module.HasFeature<MiddleFeature>()).IsFalse();
    }

    [Test]
    public async Task Compose_DoesNotTurnConditionalPresenceIntoAModuleRequirement()
    {
        var conditional = new ExplicitFeatureCatalogBuilder()
            .Add(m => new RootFeature(m), Metadata("Conditional"), dependencyOf: [typeof(LeafFeature)])
            .CreateExport(new("conditional", 1));
        var provider = Export("provider", m => new LeafFeature(m), "Leaf");
        var catalog = new FeatureModuleCatalogBuilder().Add(conditional).Add(provider).Build();
        var module = new Module(new ServiceCollection(), catalog, CompositionPolicy.ValidatedV1);
        AttachEvents(module);
        var root = module.Configure<RootFeature>();
        module.Configure<LeafFeature>();

        module.Apply();

        await Assert.That(root.Applications).IsEqualTo(1);
        var absentCatalog = new FeatureModuleCatalogBuilder().Add(conditional).Build();
        var absent = new Module(new ServiceCollection(), absentCatalog, CompositionPolicy.ValidatedV1);
        AttachEvents(absent);
        var excluded = absent.Configure<RootFeature>();
        absent.Apply();
        await Assert.That(excluded.Applications).IsEqualTo(0);
        await Assert.That(absent.HasFeature<LeafFeature>()).IsFalse();
    }

    [Test]
    public async Task CreateExport_SnapshotsFeatureMetadataDependenciesAndBuilderContents()
    {
        var metadata = Metadata("Root", "Original display");
        var dependencies = new List<Type> { typeof(LeafFeature) };
        var builder = new ExplicitFeatureCatalogBuilder().Add(m => new RootFeature(m), metadata, dependencies);
        var export = builder.CreateExport(new("consumer", 1, [new ModuleRequirement("provider", 1)]));
        metadata.Name = "CallerMutation";
        metadata.DisplayName = "Caller mutation";
        dependencies.Clear();
        builder.Add(m => new LaterFeature(m), Metadata("Later"));
        var provider = Export("provider", m => new LeafFeature(m), "Leaf");

        var catalog = new FeatureModuleCatalogBuilder().Add(export).Add(provider).Build();

        await Assert.That(catalog.FeatureTypes.Contains(typeof(LaterFeature))).IsFalse();
        var services = new ServiceCollection();
        var module = new Module(services, catalog, CompositionPolicy.ValidatedV1);
        AttachEvents(module);
        module.Configure<RootFeature>();
        module.Apply();
        await Assert.That(module.HasFeature<LeafFeature>()).IsTrue();
        await Assert.That(Registry(services).Find("Exports.Root")!.DisplayName).IsEqualTo("Original display");
        await Assert.That(Registry(services).Find("Exports.CallerMutation")).IsNull();
    }

    [Test]
    public async Task Add_RepeatedAdditionOfTheCurrentlySelectedExportObjectIsIdempotent()
    {
        var export = Export("module", m => new RootFeature(m), "Root");

        var catalog = new FeatureModuleCatalogBuilder().Add(export).Add(export).Add(export).Build();

        await Assert.That(catalog.FeatureTypes.SequenceEqual([typeof(RootFeature)])).IsTrue();
    }

    [Test]
    public async Task Add_DifferentObjectsClaimingTheSameIdentityAreRejectedEvenWithMatchingDeclarations()
    {
        var first = Export("module", static m => new RootFeature(m), "Root");
        var second = Export("module", static m => new RootFeature(m), "Root");

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(first).Add(second).Build(), "module");
    }

    [Test]
    public async Task Add_DifferentBindingsWithTheSameModuleIdAndVersionCannotBeSilentlyDiscarded()
    {
        var firstCalls = 0;
        var conflictingCalls = 0;
        var first = Export("module", m => { firstCalls++; return new RootFeature(m); }, "Root");
        var conflicting = Export("module", m => { conflictingCalls++; return new LeafFeature(m); }, "Leaf");

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(first).Add(conflicting).Build(), "module");
        await Assert.That(firstCalls + conflictingCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Add_DoesNotImplicitlySelectAHigherContractVersion()
    {
        var first = Export("module", m => new RootFeature(m), "Root", contractVersion: 1);
        var newer = Export("module", m => new LeafFeature(m), "Leaf", contractVersion: 2);

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(first).Add(newer).Build(), "module");
    }

    [Test]
    public async Task Compose_MissingRequiredModuleNamesBothTheRequesterAndMissingOwner()
    {
        var export = Export("consumer", m => new RootFeature(m), "Root", requirements: [new ModuleRequirement("missing-provider", 1)]);

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(export).Build(), "consumer", "missing-provider");
    }

    [Test]
    public async Task Compose_RejectsAnIncorrectRequiredContractVersion()
    {
        var consumer = Export("consumer", m => new RootFeature(m), "Root", requirements: [new ModuleRequirement("provider", 2)]);
        var provider = Export("provider", m => new LeafFeature(m), "Leaf", contractVersion: 1);

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(consumer).Add(provider).Build(), "consumer", "provider");
    }

    [Test]
    public async Task Compose_RejectsAnAbsentRequiredCapability()
    {
        var consumer = Export("consumer", m => new RootFeature(m), "Root",
            requirements: [new ModuleRequirement("provider", 1, ["feature.required"])]);
        var provider = Export("provider", m => new LeafFeature(m), "Leaf", capabilities: ["feature.other"]);

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(consumer).Add(provider).Build(),
            "consumer", "provider", "feature.required");
    }

    [Test]
    public async Task Compose_ValidatesSchemaBeforeMergingConflictingBindingsOrActivatingFactories()
    {
        var calls = 0;
        var valid = Export("valid", m => { calls++; return new RootFeature(m); }, "Root");
        var unsupported = Export("unsupported", m => { calls++; return new RootFeature(m); }, "Root",
            schemaVersion: ModuleExportSchema.CurrentVersion + 1);

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(valid).Add(unsupported).Build(), "unsupported", "schema");
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task Compose_ModuleCyclesIdentifyTheWholeRequiredModulePath()
    {
        var first = Export("module-a", m => new RootFeature(m), "Root", requirements: [new ModuleRequirement("module-b", 1)]);
        var second = Export("module-b", m => new MiddleFeature(m), "Middle", requirements: [new ModuleRequirement("module-c", 1)]);
        var third = Export("module-c", m => new LeafFeature(m), "Leaf", requirements: [new ModuleRequirement("module-a", 1)]);

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(first).Add(second).Add(third).Build(),
            "module-a", "module-b", "module-c");
    }

    [Test]
    public async Task Compose_GlobalStrongGraphRejectsAMissingFeatureBindingEvenWhenModulesExist()
    {
        var consumer = Export("consumer", m => new RootFeature(m), "Root", [typeof(LeafFeature)],
            requirements: [new ModuleRequirement("provider", 1)]);
        var provider = Export("provider", m => new MiddleFeature(m), "Middle");

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(consumer).Add(provider).Build(),
            "consumer", nameof(RootFeature), nameof(LeafFeature));
    }

    [Test]
    public async Task Compose_RejectsStrongFeatureCyclesWithoutInvokingFactories()
    {
        var calls = 0;

        await AssertFailureAsync(() =>
        {
            var export = new ExplicitFeatureCatalogBuilder()
                .Add(m => { calls++; return new RootFeature(m); }, Metadata("Root"), [typeof(LeafFeature)])
                .Add(m => { calls++; return new LeafFeature(m); }, Metadata("Leaf"), [typeof(RootFeature)])
                .CreateExport(new("cycle", 1));
            new FeatureModuleCatalogBuilder().Add(export).Build();
        }, nameof(RootFeature), nameof(LeafFeature));
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task Compose_RejectsAStrongFeatureCycleAcrossAnAcyclicModuleGraph()
    {
        var consumer = Export("consumer", m => new RootFeature(m), "Root", [typeof(LeafFeature)],
            requirements: [new ModuleRequirement("provider", 1)]);
        var provider = Export("provider", m => new LeafFeature(m), "Leaf", [typeof(RootFeature)]);

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(consumer).Add(provider).Build(),
            "provider", "consumer", nameof(LeafFeature), nameof(RootFeature), "requirement closure");
    }

    [Test]
    public async Task Compose_FeatureTypeCollisionNamesBothExportOwners()
    {
        var first = Export("owner-a", m => new RootFeature(m), "First");
        var second = Export("owner-b", m => new RootFeature(m), "Second");

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(first).Add(second).Build(),
            "owner-a", "owner-b", nameof(RootFeature));
    }

    [Test]
    public async Task Compose_InstalledMetadataIdentityCollisionNamesBothExportOwners()
    {
        var first = Export("owner-a", m => new RootFeature(m), "Duplicate");
        var second = Export("owner-b", m => new LeafFeature(m), "Duplicate");

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(first).Add(second).Build(),
            "owner-a", "owner-b", "Exports.Duplicate");
    }

    [Test]
    public async Task Compose_HostedServiceCollisionNamesBothExportOwners()
    {
        var first = new ExplicitFeatureCatalogBuilder().AddHostedService<FirstWorker>().CreateExport(new("owner-a", 1));
        var second = new ExplicitFeatureCatalogBuilder().AddHostedService<FirstWorker>().CreateExport(new("owner-b", 1));

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(first).Add(second).Build(),
            "owner-a", "owner-b", nameof(FirstWorker));
    }

    [Test]
    public async Task Compose_UsesStableReadyModuleOrderAndRetainsEachFragmentsDeclarationOrder()
    {
        var blocked = new ExplicitFeatureCatalogBuilder().Add(m => new RootFeature(m), Metadata("Root"))
            .AddHostedService<FirstWorker>().CreateExport(new("blocked", 1, [new ModuleRequirement("provider", 1)]));
        var independent = new ExplicitFeatureCatalogBuilder()
            .Add(m => new FirstFeature(m), Metadata("First"))
            .Add(m => new SecondFeature(m), Metadata("Second"))
            .AddHostedService<SecondWorker>().AddHostedService<ThirdWorker>()
            .CreateExport(new("independent", 1));
        var provider = new ExplicitFeatureCatalogBuilder().Add(m => new LeafFeature(m), Metadata("Leaf"))
            .AddHostedService<FourthWorker>().CreateExport(new("provider", 1));

        var catalog = new FeatureModuleCatalogBuilder().Add(blocked).Add(independent).Add(provider).Build();

        await Assert.That(catalog.FeatureTypes.SequenceEqual([
            typeof(FirstFeature), typeof(SecondFeature), typeof(LeafFeature), typeof(RootFeature)
        ])).IsTrue();
        await Assert.That(catalog.HostedServiceTypes.SequenceEqual([
            typeof(SecondWorker), typeof(ThirdWorker), typeof(FourthWorker), typeof(FirstWorker)
        ])).IsTrue();
    }

    [Test]
    public async Task Replace_RequiresTheCurrentlySelectedExpectedObject()
    {
        var selected = Export("module", m => new RootFeature(m), "Root");
        var lookalike = Export("module", m => new RootFeature(m), "Root");
        var replacement = Export("module", m => new LeafFeature(m), "Leaf");
        var builder = new FeatureModuleCatalogBuilder().Add(selected);

        await AssertFailureAsync(() => builder.Replace(lookalike, replacement).Build(), "module");
        await Assert.That(builder.Build().FeatureTypes.SequenceEqual([typeof(RootFeature)])).IsTrue();
    }

    [Test]
    public async Task Replace_CannotAddAnAbsentModuleOrChangeItsLogicalId()
    {
        var selected = Export("module", m => new RootFeature(m), "Root");
        var sameIdReplacement = Export("module", m => new LeafFeature(m), "Leaf");
        var otherIdReplacement = Export("other", m => new LeafFeature(m), "Leaf");

        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Replace(selected, sameIdReplacement).Build(), "module");
        await AssertFailureAsync(() => new FeatureModuleCatalogBuilder().Add(selected).Replace(selected, otherIdReplacement).Build(), "module", "other");
    }

    [Test]
    public async Task Replace_PreservesItsSlotAndDoesNotMutateAlreadyBuiltCatalogs()
    {
        var before = Export("before", m => new FirstFeature(m), "First");
        var selected = Export("selected", m => new RootFeature(m), "Root");
        var after = Export("after", m => new SecondFeature(m), "Second");
        var replacement = Export("selected", m => new LeafFeature(m), "Leaf");
        var builder = new FeatureModuleCatalogBuilder().Add(before).Add(selected).Add(after);
        var original = builder.Build();

        var replaced = builder.Replace(selected, replacement).Build();

        await Assert.That(original.FeatureTypes.SequenceEqual([typeof(FirstFeature), typeof(RootFeature), typeof(SecondFeature)])).IsTrue();
        await Assert.That(replaced.FeatureTypes.SequenceEqual([typeof(FirstFeature), typeof(LeafFeature), typeof(SecondFeature)])).IsTrue();
    }

    [Test]
    public async Task Add_AnEarlierReplacedObjectIsNotSilentlyTreatedAsAnIdempotentAddition()
    {
        var original = Export("module", m => new RootFeature(m), "Root");
        var replacement = Export("module", m => new LeafFeature(m), "Leaf");
        var builder = new FeatureModuleCatalogBuilder().Add(original).Replace(original, replacement);

        await AssertFailureAsync(() => builder.Add(original).Build(), "module");
        await Assert.That(builder.Add(replacement).Build().FeatureTypes.SequenceEqual([typeof(LeafFeature)])).IsTrue();
        await AssertFailureAsync(() => builder.Replace(original, replacement).Build(), "module");
    }

    [Test]
    public async Task Replace_RevalidatesDependentContractVersions()
    {
        var provider = Export("provider", m => new LeafFeature(m), "Leaf");
        var replacement = Export("provider", m => new LeafFeature(m), "Leaf", contractVersion: 2);
        var consumer = Export("consumer", m => new RootFeature(m), "Root", [typeof(LeafFeature)],
            requirements: [new ModuleRequirement("provider", 1)]);
        var builder = new FeatureModuleCatalogBuilder().Add(consumer).Add(provider);
        builder.Build();

        await AssertFailureAsync(() => builder.Replace(provider, replacement).Build(), "consumer", "provider");
    }

    [Test]
    public async Task Replace_RevalidatesDependentCapabilities()
    {
        var provider = Export("provider", m => new LeafFeature(m), "Leaf", capabilities: ["feature.required"]);
        var replacement = Export("provider", m => new LeafFeature(m), "Leaf", capabilities: ["feature.other"]);
        var consumer = Export("consumer", m => new RootFeature(m), "Root", [typeof(LeafFeature)],
            requirements: [new ModuleRequirement("provider", 1, ["feature.required"])]);
        var builder = new FeatureModuleCatalogBuilder().Add(consumer).Add(provider);
        builder.Build();

        await AssertFailureAsync(() => builder.Replace(provider, replacement).Build(), "consumer", "provider", "feature.required");
    }

    [Test]
    public async Task Replace_RevalidatesItsOwnNewRequirementsBeforeMerging()
    {
        var original = Export("module", m => new RootFeature(m), "Root");
        var replacement = Export("module", m => new RootFeature(m), "Root", requirements: [new ModuleRequirement("new-required-module", 1)]);
        var builder = new FeatureModuleCatalogBuilder().Add(original);
        builder.Build();

        await AssertFailureAsync(() => builder.Replace(original, replacement).Build(), "module", "new-required-module");
    }

    [Test]
    public async Task Replace_RevalidatesStrongFeatureCoverageAndLeavesEarlierCatalogsUsable()
    {
        var provider = Export("provider", m => new LeafFeature(m), "Leaf");
        var consumer = Export("consumer", m => new RootFeature(m), "Root", [typeof(LeafFeature)],
            requirements: [new ModuleRequirement("provider", 1)]);
        var replacement = Export("provider", m => new MiddleFeature(m), "Middle");
        var builder = new FeatureModuleCatalogBuilder().Add(consumer).Add(provider);
        var originalCatalog = builder.Build();

        await AssertFailureAsync(() => builder.Replace(provider, replacement).Build(), "consumer", nameof(RootFeature), nameof(LeafFeature));
        var module = new Module(new ServiceCollection(), originalCatalog, CompositionPolicy.ValidatedV1);
        AttachEvents(module);
        var root = module.Configure<RootFeature>();
        module.Apply();
        await Assert.That(root.Applications).IsEqualTo(1);
        await Assert.That(module.HasFeature<LeafFeature>()).IsTrue();
    }

    [Test]
    public async Task Compose_ReturnedCatalogsAreSnapshotsOfTheExportSelection()
    {
        var first = Export("first", m => new FirstFeature(m), "First");
        var later = Export("later", m => new LaterFeature(m), "Later");
        var builder = new FeatureModuleCatalogBuilder().Add(first);
        var original = builder.Build();

        var extended = builder.Add(later).Build();

        await Assert.That(original.FeatureTypes.SequenceEqual([typeof(FirstFeature)])).IsTrue();
        await Assert.That(extended.FeatureTypes.SequenceEqual([typeof(FirstFeature), typeof(LaterFeature)])).IsTrue();
    }

    [Test]
    public async Task ComposedCatalog_PreservesFactoryOverridesRuntimeActionsAndIndependentHosts()
    {
        var originalFactoryCalls = 0;
        var overrideCalls = 0;
        var root = Export("consumer", m =>
        {
            originalFactoryCalls++;
            return new RootFeature(m);
        }, "Root", [typeof(LeafFeature)], requirements: [new ModuleRequirement("provider", 1)]);
        var provider = Export("provider", m => new LeafFeature(m), "Leaf");
        var catalog = new FeatureModuleCatalogBuilder().Add(root).Add(provider).Build();
        var firstServices = new ServiceCollection();
        var secondServices = new ServiceCollection();
        var first = new Module(firstServices, catalog, CompositionPolicy.ValidatedV1);
        var second = new Module(secondServices, catalog, CompositionPolicy.ValidatedV1);
        AttachEvents(first);
        AttachEvents(second);
        var firstRoot = first.Configure(m =>
        {
            overrideCalls++;
            return new RootFeature(m) { Value = "first-host" };
        }, feature => feature.Actions.Add("first-action"));
        first.Configure<RootFeature>(feature => feature.Actions.Add("second-action"));
        var secondRoot = second.Configure<RootFeature>(feature => feature.Value = "second-host");
        first.Apply();
        second.Apply();
        Registry(firstServices).Find("Exports.Root")!.DisplayName = "first-host-mutation";

        await Assert.That(originalFactoryCalls).IsEqualTo(1);
        await Assert.That(overrideCalls).IsEqualTo(1);
        await Assert.That(ReferenceEquals(firstRoot, secondRoot)).IsFalse();
        await Assert.That(firstRoot.Value).IsEqualTo("first-host");
        await Assert.That(secondRoot.Value).IsEqualTo("second-host");
        await Assert.That(firstRoot.Actions.SequenceEqual(["first-action", "second-action"])).IsTrue();
        await Assert.That(secondRoot.Actions).IsEmpty();
        await Assert.That(Registry(secondServices).Find("Exports.Root")!.DisplayName).IsEqualTo("Root");
        await Assert.That(firstRoot.Configurations == 1 && secondRoot.Configurations == 1).IsTrue();
        await Assert.That(firstRoot.Applications == 1 && secondRoot.Applications == 1).IsTrue();
    }

    private static FeatureModuleExport Export<TFeature>(string moduleId, Func<IModule, TFeature> factory, string name,
        IEnumerable<Type>? dependencies = null, IEnumerable<ModuleRequirement>? requirements = null,
        IEnumerable<string>? capabilities = null, int contractVersion = 1, int schemaVersion = ModuleExportSchema.CurrentVersion)
        where TFeature : class, IFeature => new ExplicitFeatureCatalogBuilder().Add(factory, Metadata(name), dependencies)
        .CreateExport(new ModuleExportDescriptor(moduleId, contractVersion, requirements, capabilities, schemaVersion));

    private static FeatureDescriptor Metadata(string name, string? displayName = null) => new(name, "Exports", displayName ?? name);
    private static List<string> AttachEvents(IModule module)
    {
        var events = new List<string>();
        module.Properties["events"] = events;
        return events;
    }
    private static IInstalledFeatureRegistry Registry(ServiceCollection services) =>
        (IInstalledFeatureRegistry)services.Last(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance!;

    private static async Task AssertFailureAsync(Action action, params string[] fragments)
    {
        Exception? failure = null;
        try { action(); }
        catch (Exception exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure is InvalidOperationException).IsTrue();
        foreach (var fragment in fragments)
            await Assert.That(failure!.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)).IsTrue()
                .Because($"The diagnostic must identify '{fragment}', received '{failure.Message}'.");
    }

    public abstract class RecordingFeature(IModule module, string name) : FeatureBase(module)
    {
        public int Configurations { get; private set; }
        public int Applications { get; private set; }
        public override void Configure()
        {
            Configurations++;
            if (Module.Properties.TryGetValue("events", out var events))
                ((List<string>)events).Add($"{name}.configure");
        }
        public override void Apply()
        {
            Applications++;
            if (Module.Properties.TryGetValue("events", out var events))
                ((List<string>)events).Add($"{name}.apply");
        }
    }
    public sealed class RootFeature(IModule module) : RecordingFeature(module, "root")
    {
        public string Value { get; set; } = "catalog";
        public List<string> Actions { get; } = [];
    }
    public sealed class LeafFeature(IModule module) : RecordingFeature(module, "leaf");
    public sealed class MiddleFeature(IModule module) : RecordingFeature(module, "middle");
    public sealed class LaterFeature(IModule module) : RecordingFeature(module, "later");
    public sealed class FirstFeature(IModule module) : RecordingFeature(module, "first");
    public sealed class SecondFeature(IModule module) : RecordingFeature(module, "second");

    public abstract class Worker : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    public sealed class FirstWorker : Worker;
    public sealed class SecondWorker : Worker;
    public sealed class ThirdWorker : Worker;
    public sealed class FourthWorker : Worker;
}