using Elsa.Features.Abstractions;
using Elsa.Features.Contracts;
using Elsa.Features.Implementations;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Features.UnitTests;

/// <summary>
/// Exercises the explicit metadata contract and the policies shared with managed discovery.
/// </summary>
public class ExplicitCompositionTests
{
    [Test]
    public async Task Catalog_CopiesInputsAndDoesNotChangeWhenItsBuilderIsReused()
    {
        var dependencies = new List<Type> { typeof(LeafFeature) };
        var metadata = Metadata("Root", "Original display", "Original description");
        var builder = new ExplicitFeatureCatalogBuilder()
            .Add(m => new PlainFeature(m), metadata, dependencies)
            .Add(m => new LeafFeature(m), Metadata("Leaf"));
        var catalog = builder.Build();
        dependencies.Clear();
        metadata.Name = "Changed";
        metadata.DisplayName = "Changed";
        metadata.Description = "Changed";
        builder.Add(m => new MissingTargetFeature(m), Metadata("Later"));
        var services = new ServiceCollection();
        var module = new Module(services, catalog);
        module.Configure<PlainFeature>();

        module.Apply();

        await Assert.That(module.HasFeature<LeafFeature>()).IsTrue();
        var installed = Registry(services).Find("Test.Root")!;
        await Assert.That(installed.DisplayName).IsEqualTo("Original display");
        await Assert.That(installed.Description).IsEqualTo("Original description");
        await Assert.That(Registry(services).Find("Test.Changed")).IsNull();
        var failure = CaptureFailure(() => module.Configure<MissingTargetFeature>());
        await Assert.That(failure.Message.Contains(nameof(MissingTargetFeature), StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Catalog_CopiesConditionalInputs()
    {
        var targets = new List<Type> { typeof(MissingTargetFeature) };
        var builder = new ExplicitFeatureCatalogBuilder()
            .Add(m => new PlainFeature(m), Metadata("Conditional"), dependencyOf: targets);
        var catalog = builder.Build();
        targets.Clear();
        var module = new Module(new ServiceCollection(), catalog, CompositionPolicy.ValidatedV1);
        var feature = module.Configure<PlainFeature>();

        module.Apply();

        await Assert.That(module.HasFeature<PlainFeature>()).IsFalse();
        await Assert.That(feature.Applications).IsEqualTo(0);
    }

    [Test]
    public async Task Catalog_InstalledMetadataAndFeatureInstancesAreIsolatedBetweenHosts()
    {
        var catalog = new ExplicitFeatureCatalogBuilder().Add(m => new PlainFeature(m), Metadata("Plain")).Build();
        var firstServices = new ServiceCollection();
        var secondServices = new ServiceCollection();
        var first = new Module(firstServices, catalog);
        var second = new Module(secondServices, catalog);
        var firstFeature = first.Configure<PlainFeature>(f => f.Value = "first");
        var secondFeature = second.Configure<PlainFeature>(f => f.Value = "second");
        first.Apply();
        second.Apply();
        Registry(firstServices).Find("Test.Plain")!.DisplayName = "Mutated first host";

        await Assert.That(ReferenceEquals(firstFeature, secondFeature)).IsFalse();
        await Assert.That(firstFeature.Value).IsEqualTo("first");
        await Assert.That(secondFeature.Value).IsEqualTo("second");
        await Assert.That(Registry(secondServices).Find("Test.Plain")!.DisplayName).IsEqualTo("Plain");
        var thirdServices = new ServiceCollection();
        var third = new Module(thirdServices, catalog);
        third.Configure<PlainFeature>();
        third.Apply();
        await Assert.That(Registry(thirdServices).Find("Test.Plain")!.DisplayName).IsEqualTo("Plain");
    }

    [Test]
    public async Task Catalog_RejectsDuplicateFeatureType()
    {
        var failure = CaptureFailure(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new PlainFeature(m), Metadata("First"))
            .Add(m => new PlainFeature(m), Metadata("Second")).Build());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(PlainFeature), StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Catalog_RejectsDuplicateInstalledIdentity()
    {
        var failure = CaptureFailure(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new PlainFeature(m), Metadata("Duplicate"))
            .Add(m => new LeafFeature(m), Metadata("Duplicate")).Build());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains("Test.Duplicate", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Catalog_MissingStrongDependencyReportsTheEntirePath()
    {
        var failure = CaptureFailure(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new RootFeature(m), Metadata("Root"), [typeof(LeftFeature)])
            .Add(m => new LeftFeature(m), Metadata("Left"), [typeof(MissingTargetFeature)]).Build());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(RootFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(LeftFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(MissingTargetFeature), StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Catalog_DependencyCycleReportsEveryMember()
    {
        var failure = CaptureFailure(() => new ExplicitFeatureCatalogBuilder()
            .Add(m => new RootFeature(m), Metadata("Root"), [typeof(LeftFeature)])
            .Add(m => new LeftFeature(m), Metadata("Left"), [typeof(LeafFeature)])
            .Add(m => new LeafFeature(m), Metadata("Leaf"), [typeof(RootFeature)]).Build());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(RootFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(LeftFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(LeafFeature), StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(typeof(OpenFeature<>))]
    [Arguments(typeof(AbstractFeature))]
    [Arguments(typeof(string))]
    public async Task Catalog_RejectsTypesThatCannotRepresentAClosedFeature(Type type)
    {
        var failure = CaptureFailure(() => new ExplicitFeatureCatalogBuilder()
            .Add(type, m => new PlainFeature(m), Metadata("Invalid")).Build());

        await Assert.That(failure is InvalidOperationException or ArgumentException).IsTrue();
    }

    [Test]
    public async Task Configure_RejectsWrongFactoryIdentityAndFaultsTheExplicitModule()
    {
        var catalog = new ExplicitFeatureCatalogBuilder()
            .Add(typeof(PlainFeature), m => new LeafFeature(m), Metadata("Plain")).Build();
        var module = new Module(new ServiceCollection(), catalog);

        var failure = CaptureFailure(() => module.Configure<PlainFeature>());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(PlainFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(LeafFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Configure_FactoryOverrideStillRequiresCatalogCoverage()
    {
        var module = new Module(new ServiceCollection(), new ExplicitFeatureCatalogBuilder().Build());
        var factoryCalled = false;

        var failure = CaptureFailure(() => module.Configure(m =>
        {
            factoryCalled = true;
            return new PlainFeature(m);
        }));

        await Assert.That(factoryCalled).IsFalse();
        await Assert.That(failure.Message.Contains(nameof(PlainFeature), StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Configure_ReusesCustomFactoryAndRunsEveryAction(CompositionPolicy policy)
    {
        var catalogFactoryCalls = 0;
        var overrideCalls = 0;
        var catalog = new ExplicitFeatureCatalogBuilder().Add(m =>
        {
            catalogFactoryCalls++;
            return new PlainFeature(m);
        }, Metadata("Plain")).Build();
        var module = new Module(new ServiceCollection(), catalog, policy);
        var first = module.Configure(m =>
        {
            overrideCalls++;
            return new PlainFeature(m) { Value = "custom" };
        }, f => f.Actions.Add("first"));
        var second = module.Configure<PlainFeature>(_ => throw new InvalidOperationException("Must reuse the first instance."),
            f => f.Actions.Add("second"));
        module.Configure<PlainFeature>(f => f.Actions.Add("third"));

        module.Apply();

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(catalogFactoryCalls).IsEqualTo(0);
        await Assert.That(overrideCalls).IsEqualTo(1);
        await Assert.That(first.Value).IsEqualTo("custom");
        await Assert.That(first.Actions.SequenceEqual(["first", "second", "third"])).IsTrue();
        await Assert.That(first.Configurations).IsEqualTo(1);
        await Assert.That(first.Applications).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, CompositionPolicy.LegacyV1)]
    [Arguments(false, CompositionPolicy.ValidatedV1)]
    [Arguments(true, CompositionPolicy.LegacyV1)]
    [Arguments(true, CompositionPolicy.ValidatedV1)]
    public async Task Apply_TransitiveDiamondConfiguresAndAppliesEachInstanceOnce(bool explicitCatalog, CompositionPolicy policy)
    {
        var module = CreateGraphModule(new ServiceCollection(), explicitCatalog, policy);
        var events = new List<string>();
        module.Properties["events"] = events;
        var root = module.Configure<RootFeature>();

        module.Apply();

        await Assert.That(events.Where(x => x.EndsWith(".apply", StringComparison.Ordinal)).SequenceEqual([
            "LeafFeature.apply", "LeftFeature.apply", "RightFeature.apply", "RootFeature.apply"
        ])).IsTrue();
        await Assert.That(events.Count(x => x == "LeafFeature.configure")).IsEqualTo(1);
        await Assert.That(root.Configurations).IsEqualTo(1);
        await Assert.That(root.Applications).IsEqualTo(1);
        await Assert.That(module.HasFeature<LeafFeature>()).IsTrue();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Apply_ReusesConfiguredInstancesAcrossBatches(CompositionPolicy policy)
    {
        var services = new ServiceCollection();
        var module = CreateGraphModule(services, true, policy);
        var root = module.Configure<RootFeature>();
        module.Apply();
        var leaf = module.Configure<LeafFeature>(f => f.Actions.Add("late-action"));
        await Assert.That(leaf.Configurations).IsEqualTo(1);
        await Assert.That(root.Applications).IsEqualTo(1);

        module.Apply();

        await Assert.That(root.Configurations).IsEqualTo(1);
        await Assert.That(root.Applications).IsEqualTo(2);
        await Assert.That(leaf.Configurations).IsEqualTo(1);
        await Assert.That(leaf.Applications).IsEqualTo(2);
        await Assert.That(leaf.Actions.SequenceEqual(["late-action"])).IsTrue();
        await Assert.That(services.Count(x => x.ServiceType == typeof(IInstalledFeatureRegistry))).IsEqualTo(2);
    }

    [Test]
    [Arguments(false, CompositionPolicy.LegacyV1)]
    [Arguments(false, CompositionPolicy.ValidatedV1)]
    [Arguments(true, CompositionPolicy.LegacyV1)]
    [Arguments(true, CompositionPolicy.ValidatedV1)]
    public async Task Apply_ProcessesLateChainsAndTheirStrongDependencies(bool explicitCatalog, CompositionPolicy policy)
    {
        var builder = BasicBuilder()
            .Add(m => new LateRootFeature(m), Metadata("LateRoot"))
            .Add(m => new LateMiddleFeature(m), Metadata("LateMiddle"))
            .Add(m => new LateLeafFeature(m), Metadata("LateLeaf"), [typeof(LeafFeature)]);
        var module = explicitCatalog ? new Module(new ServiceCollection(), builder.Build(), policy) : new Module(new ServiceCollection(), policy);
        var events = new List<string>();
        module.Properties["events"] = events;
        module.Configure<LateRootFeature>();

        module.Apply();

        await Assert.That(events.Where(x => x.EndsWith(".apply", StringComparison.Ordinal)).SequenceEqual([
            "LateRootFeature.apply", "LateMiddleFeature.apply", "LeafFeature.apply", "LateLeafFeature.apply"
        ])).IsTrue();
        await Assert.That(events.Count(x => x == "LateMiddleFeature.configure")).IsEqualTo(1);
        await Assert.That(events.Count(x => x == "LeafFeature.configure")).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, CompositionPolicy.LegacyV1)]
    [Arguments(false, CompositionPolicy.ValidatedV1)]
    [Arguments(true, CompositionPolicy.LegacyV1)]
    [Arguments(true, CompositionPolicy.ValidatedV1)]
    public async Task Apply_ConditionalChainsFollowTheExplicitlySelectedPolicy(bool explicitCatalog, CompositionPolicy policy)
    {
        var module = CreateConditionalModule(new ServiceCollection(), explicitCatalog, policy);
        var first = module.Configure<ConditionalAFeature>();
        var second = module.Configure<ConditionalBFeature>();

        module.Apply();

        await Assert.That(module.HasFeature<ConditionalAFeature>()).IsEqualTo(policy == CompositionPolicy.LegacyV1);
        await Assert.That(module.HasFeature<ConditionalBFeature>()).IsFalse();
        await Assert.That(module.HasFeature<MissingTargetFeature>()).IsFalse();
        await Assert.That(first.Applications).IsEqualTo(policy == CompositionPolicy.LegacyV1 ? 1 : 0);
        await Assert.That(second.Applications).IsEqualTo(0);
        await Assert.That(first.Configurations).IsEqualTo(1);
        await Assert.That(second.Configurations).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Apply_ValidatedPolicyRejectsAStrongDependencyExcludedByConditionalAdmission(bool explicitCatalog)
    {
        var module = CreateConditionalModule(new ServiceCollection(), explicitCatalog, CompositionPolicy.ValidatedV1);
        var root = module.Configure<StrongConditionalRootFeature>();

        var failure = CaptureFailure(module.Apply);

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(StrongConditionalRootFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(ConditionalAFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(root.Applications).IsEqualTo(0);
        if (explicitCatalog)
            await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Apply_LegacyPolicyRetainsAStrongDependentOfAnExcludedConditionalFeature(bool explicitCatalog)
    {
        var module = CreateConditionalModule(new ServiceCollection(), explicitCatalog, CompositionPolicy.LegacyV1);
        var root = module.Configure<StrongConditionalRootFeature>();

        module.Apply();

        await Assert.That(root.Applications).IsEqualTo(1);
        await Assert.That(module.HasFeature<ConditionalAFeature>()).IsFalse();
    }

    [Test]
    [Arguments(false, CompositionPolicy.LegacyV1)]
    [Arguments(false, CompositionPolicy.ValidatedV1)]
    [Arguments(true, CompositionPolicy.LegacyV1)]
    [Arguments(true, CompositionPolicy.ValidatedV1)]
    public async Task Apply_LateConditionalFeaturesUseTheSamePolicyAsInitialFeatures(bool explicitCatalog, CompositionPolicy policy)
    {
        var module = CreateConditionalModule(new ServiceCollection(), explicitCatalog, policy);
        var introducing = module.Configure<LateConditionalRootFeature>();

        module.Apply();

        await Assert.That(introducing.Applications).IsEqualTo(1);
        await Assert.That(module.HasFeature<ConditionalAFeature>()).IsEqualTo(policy == CompositionPolicy.LegacyV1);
        var events = (List<string>)module.Properties["late-events"];
        await Assert.That(events.Contains("conditional.apply")).IsEqualTo(policy == CompositionPolicy.LegacyV1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Apply_ValidatedPolicyRejectsLateInvalidDependenciesBeforeTheirApply(bool explicitCatalog)
    {
        var module = CreateConditionalModule(new ServiceCollection(), explicitCatalog, CompositionPolicy.ValidatedV1);
        var introducing = module.Configure<LateInvalidRootFeature>();

        var failure = CaptureFailure(module.Apply);

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(StrongConditionalRootFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(introducing.Applications).IsEqualTo(1);
        var events = (List<string>)module.Properties["late-events"];
        await Assert.That(events.Contains("strong.apply")).IsFalse();
        await Assert.That(events.Contains("conditional.apply")).IsFalse();
        if (explicitCatalog)
            await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Configure_CallbackFailureFaultsTheExplicitModuleAndRejectsRetry(CompositionPolicy policy)
    {
        var module = new Module(new ServiceCollection(), BasicBuilder().Build(), policy);
        PlainFeature? created = null;

        var failure = CaptureFailure(() => module.Configure(m => created = new PlainFeature(m), _ =>
            throw new InvalidOperationException("configuration-action-failed")));

        await Assert.That(failure.Message).IsEqualTo("configuration-action-failed");
        await Assert.That(created!.Applications).IsEqualTo(0);
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
        await Assert.That(() => module.Configure<LeafFeature>()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Apply_ConfigureCallbackFailureFaultsTheExplicitModuleBeforeAnyApply()
    {
        var catalog = new ExplicitFeatureCatalogBuilder()
            .Add(m => new ThrowingConfigureFeature(m), Metadata("ThrowingConfigure")).Build();
        var module = new Module(new ServiceCollection(), catalog, CompositionPolicy.ValidatedV1);
        var feature = module.Configure<ThrowingConfigureFeature>();

        var failure = CaptureFailure(module.Apply);

        await Assert.That(failure.Message).IsEqualTo("feature-configure-failed");
        await Assert.That(feature.Applications).IsEqualTo(0);
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
        await Assert.That(() => module.Configure<ThrowingConfigureFeature>()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Configure_NullFactoryResultReportsTheFeatureAndFaultsTheModule()
    {
        var catalog = new ExplicitFeatureCatalogBuilder()
            .Add(typeof(PlainFeature), _ => null!, Metadata("Plain")).Build();
        var module = new Module(new ServiceCollection(), catalog);

        var failure = CaptureFailure(() => module.Configure<PlainFeature>());

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(failure.Message.Contains(nameof(PlainFeature), StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains("null", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Apply_CallbackFailureFaultsTheModuleAndPreventsLaterApplications()
    {
        var catalog = BasicBuilder()
            .Add(m => new ThrowingApplyFeature(m), Metadata("ThrowingApply")).Build();
        var module = new Module(new ServiceCollection(), catalog, CompositionPolicy.ValidatedV1);
        var failing = module.Configure<ThrowingApplyFeature>();
        var later = module.Configure<PlainFeature>();

        var failure = CaptureFailure(module.Apply);

        await Assert.That(failure.Message).IsEqualTo("feature-apply-failed");
        await Assert.That(failing.Applications).IsEqualTo(1);
        await Assert.That(later.Applications).IsEqualTo(0);
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
        await Assert.That(() => module.Configure<LeafFeature>()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Apply_CannotReportSuccessWhenACallbackSwallowsAnAdmissionFailure(CompositionPolicy policy)
    {
        var services = new ServiceCollection();
        var catalog = BasicBuilder()
            .Add(m => new SwallowedAdmissionFailureFeature(m), Metadata("SwallowedAdmissionFailure")).Build();
        var module = new Module(services, catalog, policy);
        var first = module.Configure<SwallowedAdmissionFailureFeature>();
        var later = module.Configure<PlainFeature>();

        var failure = CaptureFailure(module.Apply);

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(first.CaughtFailure).IsTrue();
        await Assert.That(later.Applications).IsEqualTo(0);
        await Assert.That(services.Any(x => x.ServiceType == typeof(IInstalledFeatureRegistry))).IsFalse();
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(CompositionPolicy.LegacyV1)]
    [Arguments(CompositionPolicy.ValidatedV1)]
    public async Task Apply_CannotContinueWhenADependencyFactorySwallowsAnAdmissionFailure(CompositionPolicy policy)
    {
        var caughtFailure = false;
        LeafFeature? dependency = null;
        var catalog = new ExplicitFeatureCatalogBuilder()
            .Add(m => new PlainFeature(m), Metadata("Plain"), [typeof(LeafFeature)])
            .Add(m =>
            {
                try
                {
                    m.Configure<MissingTargetFeature>();
                }
                catch (InvalidOperationException)
                {
                    caughtFailure = true;
                }
                return dependency = new LeafFeature(m);
            }, Metadata("Leaf")).Build();
        var module = new Module(new ServiceCollection(), catalog, policy);
        var root = module.Configure<PlainFeature>();

        var failure = CaptureFailure(module.Apply);

        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(caughtFailure).IsTrue();
        await Assert.That(dependency).IsNotNull();
        await Assert.That(dependency!.Configurations).IsEqualTo(0);
        await Assert.That(dependency.Applications).IsEqualTo(0);
        await Assert.That(root.Configurations).IsEqualTo(0);
        await Assert.That(root.Applications).IsEqualTo(0);
        await Assert.That(() => module.Apply()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Apply_ProcessesALayeredDiamondWithoutDuplicatingSharedFeatures()
    {
        var instances = new List<CountingFeature>();
        var builder = new ExplicitFeatureCatalogBuilder().Add(m =>
        {
            var feature = new LeafFeature(m);
            instances.Add(feature);
            return feature;
        }, Metadata("Leaf"));
        Type[] previous = [typeof(LeafFeature)];
        // Eighteen shared layers have only 36 reachable features but hundreds of
        // thousands of dependency paths. Admission must validate shared nodes once.
        AddLayer<byte>();
        AddLayer<sbyte>();
        AddLayer<short>();
        AddLayer<ushort>();
        AddLayer<int>();
        AddLayer<uint>();
        AddLayer<long>();
        AddLayer<ulong>();
        AddLayer<float>();
        AddLayer<double>();
        AddLayer<decimal>();
        AddLayer<bool>();
        AddLayer<char>();
        AddLayer<Guid>();
        AddLayer<DateTime>();
        AddLayer<TimeSpan>();
        AddLayer<Uri>();
        AddLayer<Version>();
        var module = new Module(new ServiceCollection(), builder.Build(), CompositionPolicy.ValidatedV1);
        module.Configure<GraphNode<LeftSide, Version>>();

        module.Apply();

        await Assert.That(instances.Count).IsEqualTo(36);
        await Assert.That(instances.All(x => x.Configurations == 1 && x.Applications == 1)).IsTrue();

        void AddLayer<TLayer>()
        {
            builder.Add(m =>
            {
                var feature = new GraphNode<LeftSide, TLayer>(m);
                instances.Add(feature);
                return feature;
            }, Metadata($"{typeof(TLayer).Name}.Left"), previous);
            builder.Add(m =>
            {
                var feature = new GraphNode<RightSide, TLayer>(m);
                instances.Add(feature);
                return feature;
            }, Metadata($"{typeof(TLayer).Name}.Right"), previous);
            previous = [typeof(GraphNode<LeftSide, TLayer>), typeof(GraphNode<RightSide, TLayer>)];
        }
    }

    private static FeatureDescriptor Metadata(string name, string? displayName = null, string? description = null) =>
        new(name, "Test", displayName ?? name, description);

    private static IInstalledFeatureRegistry Registry(ServiceCollection services) =>
        (IInstalledFeatureRegistry)services.Last(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance!;

    private static Exception CaptureFailure(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }
        throw new InvalidOperationException("The operation unexpectedly succeeded.");
    }

    private static ExplicitFeatureCatalogBuilder BasicBuilder() => new ExplicitFeatureCatalogBuilder()
        .Add(m => new PlainFeature(m), Metadata("Plain"))
        .Add(m => new LeafFeature(m), Metadata("Leaf"));

    private static Module CreateGraphModule(ServiceCollection services, bool explicitCatalog, CompositionPolicy policy) => explicitCatalog
        ? new Module(services, BasicBuilder()
            .Add(m => new RootFeature(m), Metadata("Root"), [typeof(LeftFeature), typeof(RightFeature)])
            .Add(m => new LeftFeature(m), Metadata("Left"), [typeof(LeafFeature)])
            .Add(m => new RightFeature(m), Metadata("Right"), [typeof(LeafFeature)]).Build(), policy)
        : new Module(services, policy);

    private static Module CreateConditionalModule(ServiceCollection services, bool explicitCatalog, CompositionPolicy policy)
    {
        var module = explicitCatalog
            ? new Module(services, BasicBuilder()
                .Add(m => new ConditionalAFeature(m), Metadata("ConditionalA"), dependencyOf: [typeof(ConditionalBFeature)])
                .Add(m => new ConditionalBFeature(m), Metadata("ConditionalB"), dependencyOf: [typeof(MissingTargetFeature)])
                .Add(m => new MissingTargetFeature(m), Metadata("MissingTarget"))
                .Add(m => new StrongConditionalRootFeature(m), Metadata("StrongConditionalRoot"), [typeof(ConditionalAFeature)])
                .Add(m => new LateConditionalRootFeature(m), Metadata("LateConditionalRoot"))
                .Add(m => new LateInvalidRootFeature(m), Metadata("LateInvalidRoot")).Build(), policy)
            : new Module(services, policy);
        module.Properties["late-events"] = new List<string>();
        return module;
    }

    public abstract class CountingFeature(IModule module) : FeatureBase(module)
    {
        public int Configurations { get; private set; }
        public int Applications { get; private set; }
        public string Value { get; set; } = "catalog";
        public List<string> Actions { get; } = [];
        public override void Configure()
        {
            Configurations++;
            Record("configure");
        }
        public override void Apply()
        {
            Applications++;
            Record("apply");
        }
        private void Record(string operation)
        {
            if (Module.Properties.TryGetValue("events", out var events))
                ((List<string>)events).Add($"{GetType().Name}.{operation}");
        }
    }

    public sealed class PlainFeature(IModule module) : CountingFeature(module);
    public sealed class LeafFeature(IModule module) : CountingFeature(module);
    [Elsa.Features.Attributes.DependsOn(typeof(LeftFeature))]
    [Elsa.Features.Attributes.DependsOn(typeof(RightFeature))]
    public sealed class RootFeature(IModule module) : CountingFeature(module);
    [Elsa.Features.Attributes.DependsOn(typeof(LeafFeature))]
    public sealed class LeftFeature(IModule module) : CountingFeature(module);
    [Elsa.Features.Attributes.DependsOn(typeof(LeafFeature))]
    public sealed class RightFeature(IModule module) : CountingFeature(module);
    public abstract class AbstractFeature(IModule module) : CountingFeature(module);
    public sealed class OpenFeature<T>(IModule module) : CountingFeature(module);

    public sealed class LateRootFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<LateMiddleFeature>();
        }
    }
    public sealed class LateMiddleFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<LateLeafFeature>();
        }
    }
    [Elsa.Features.Attributes.DependsOn(typeof(LeafFeature))]
    public sealed class LateLeafFeature(IModule module) : CountingFeature(module);

    [Elsa.Features.Attributes.DependencyOf(typeof(ConditionalBFeature))]
    public sealed class ConditionalAFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            if (Module.Properties.TryGetValue("late-events", out var events))
                ((List<string>)events).Add("conditional.apply");
        }
    }
    [Elsa.Features.Attributes.DependencyOf(typeof(MissingTargetFeature))]
    public sealed class ConditionalBFeature(IModule module) : CountingFeature(module);
    public sealed class MissingTargetFeature(IModule module) : CountingFeature(module);
    [Elsa.Features.Attributes.DependsOn(typeof(ConditionalAFeature))]
    public sealed class StrongConditionalRootFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            if (Module.Properties.TryGetValue("late-events", out var events))
                ((List<string>)events).Add("strong.apply");
        }
    }
    public sealed class LateConditionalRootFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<ConditionalAFeature>();
        }
    }
    public sealed class LateInvalidRootFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            Module.Configure<StrongConditionalRootFeature>();
        }
    }
    public sealed class LeftSide;
    public sealed class RightSide;
    public sealed class GraphNode<TSide, TLayer>(IModule module) : CountingFeature(module);
    public sealed class ThrowingApplyFeature(IModule module) : CountingFeature(module)
    {
        public override void Apply()
        {
            base.Apply();
            throw new InvalidOperationException("feature-apply-failed");
        }
    }
    public sealed class SwallowedAdmissionFailureFeature(IModule module) : CountingFeature(module)
    {
        public bool CaughtFailure { get; private set; }
        public override void Apply()
        {
            base.Apply();
            try
            {
                Module.Configure<MissingTargetFeature>();
            }
            catch (InvalidOperationException)
            {
                CaughtFailure = true;
            }
        }
    }
    public sealed class ThrowingConfigureFeature(IModule module) : CountingFeature(module)
    {
        public override void Configure()
        {
            base.Configure();
            throw new InvalidOperationException("feature-configure-failed");
        }
    }
}