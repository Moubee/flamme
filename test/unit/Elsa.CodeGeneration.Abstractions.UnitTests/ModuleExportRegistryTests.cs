using Elsa.CodeGeneration;

namespace Elsa.CodeGeneration.Abstractions.UnitTests;

public class ModuleExportRegistryTests
{
    [Test]
    public async Task DescriptorAndRequirement_SnapshotAuthoredCollections()
    {
        var requiredCapabilities = new List<string> { "features" };
        var requirement = new ModuleRequirement("dependency", 2, requiredCapabilities);
        var requirements = new List<ModuleRequirement> { requirement };
        var capabilities = new List<string> { "composition" };
        var descriptor = new ModuleExportDescriptor("consumer", 7, requirements, capabilities);

        requiredCapabilities[0] = "changed-required";
        requirements[0] = new ModuleRequirement("changed-dependency", 1);
        capabilities[0] = "changed-capability";

        await Assert.That(descriptor.ModuleId).IsEqualTo("consumer");
        await Assert.That(descriptor.ContractVersion).IsEqualTo(7);
        await Assert.That(descriptor.SchemaVersion).IsEqualTo(ModuleExportSchema.CurrentVersion);
        await Assert.That(descriptor.Requirements.Single()).IsSameReferenceAs(requirement);
        await Assert.That(descriptor.Requirements.Single().ModuleId).IsEqualTo("dependency");
        await Assert.That(descriptor.Requirements.Single().RequiredCapabilities.SequenceEqual(["features"])).IsTrue();
        await Assert.That(descriptor.Capabilities.SequenceEqual(["composition"])).IsTrue();
        await Assert.That(() => ((IList<string>)descriptor.Capabilities)[0] = "mutation").Throws<NotSupportedException>();
        await Assert.That(() => ((IList<ModuleRequirement>)descriptor.Requirements).Clear()).Throws<NotSupportedException>();
        await Assert.That(() => ((IList<string>)requirement.RequiredCapabilities).Clear()).Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments(" leading")]
    [Arguments("trailing ")]
    public async Task Identifiers_MustBeNonblankAndTrimmed(string? moduleId)
    {
        await Assert.That(() => new ModuleExportDescriptor(moduleId!, 1)).Throws<ArgumentException>();
        await Assert.That(() => new ModuleRequirement(moduleId!, 1)).Throws<ArgumentException>();
        await Assert.That(() => new ModuleExportDescriptor("module", 1, capabilities: [moduleId!])).Throws<ArgumentException>();
        await Assert.That(() => new ModuleRequirement("module", 1, [moduleId!])).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task ContractAndSchemaVersions_MustBePositive(int version)
    {
        await Assert.That(() => new ModuleExportDescriptor("module", version)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ModuleRequirement("module", version)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ModuleExportDescriptor("module", 1, schemaVersion: version)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Descriptor_RejectsDuplicateRequirementsAndCapabilities()
    {
        await Assert.That(() => new ModuleExportDescriptor("consumer", 1,
            [new("dependency", 1), new("dependency", 2)])).Throws<ArgumentException>();
        await Assert.That(() => new ModuleExportDescriptor("module", 1, capabilities: ["features", "features"])).Throws<ArgumentException>();
        await Assert.That(() => new ModuleRequirement("dependency", 1, ["features", "features"])).Throws<ArgumentException>();
        await Assert.That(() => new ModuleExportDescriptor("module", 1, [null!])).Throws<ArgumentException>();
    }

    [Test]
    public async Task Registry_RejectsNullExportsAndReplacementArguments()
    {
        var builder = new ModuleExportRegistryBuilder<TestExport>();
        var export = Export("module");
        await Assert.That(() => builder.Add(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => builder.Replace(null!, export)).Throws<ArgumentNullException>();
        await Assert.That(() => builder.Replace(export, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new TestExport(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Build_OrdersDependenciesThenFirstAddedCurrentlyReadyModules()
    {
        var consumer = Export("consumer", requirements: [new("provider", 1)]);
        var independent = Export("independent");
        var provider = Export("provider");
        var registry = new ModuleExportRegistryBuilder<TestExport>()
            .Add(consumer).Add(independent).Add(provider).Build();

        // Independent and provider are initially ready; independent has the earlier first-add slot.
        await Assert.That(Names(registry).SequenceEqual(["independent", "provider", "consumer"])).IsTrue();
    }

    [Test]
    public async Task Build_OrdersDiamondOnceWithAuthoredTieOrderRatherThanRequirementOrder()
    {
        var registry = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("root", requirements: [new("left", 1), new("right", 1)]))
            .Add(Export("right", requirements: [new("shared", 1)]))
            .Add(Export("left", requirements: [new("shared", 1)]))
            .Add(Export("shared"))
            .Build();

        await Assert.That(Names(registry).SequenceEqual(["shared", "right", "left", "root"])).IsTrue();
    }

    [Test]
    public async Task Add_CurrentObjectIsIdempotent()
    {
        var export = Export("module");
        var registry = new ModuleExportRegistryBuilder<TestExport>().Add(export).Add(export).Build();

        await Assert.That(registry.Exports.Count).IsEqualTo(1);
        await Assert.That(registry.Exports.Single()).IsSameReferenceAs(export);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Add_DistinctObjectWithSameIdentityIsAConflictEvenAtSameRevision(int revision)
    {
        var builder = new ModuleExportRegistryBuilder<TestExport>().Add(Export("module"));
        await AssertInvalid(() => builder.Add(Export("module", revision)), "module", "different export object");
    }

    [Test]
    public async Task Replace_RequiresExactCurrentObjectAndRetainsItsFirstAddSlot()
    {
        var original = Export("first");
        var replacement = Export("first", 2);
        var other = Export("second");
        var builder = new ModuleExportRegistryBuilder<TestExport>().Add(original).Add(other);
        var previous = builder.Build();

        builder.Replace(original, replacement).Add(replacement);
        var current = builder.Build();

        await Assert.That(Names(current).SequenceEqual(["first", "second"])).IsTrue();
        await Assert.That(current.Exports[0]).IsSameReferenceAs(replacement);
        await Assert.That(previous.Exports[0]).IsSameReferenceAs(original);
        await AssertInvalid(() => builder.Replace(original, Export("first", 3)), "first", "expected export object");
        await AssertInvalid(() => builder.Add(original), "first", "different export object");
        await AssertInvalid(() => builder.Replace(Export("first", 2), Export("first", 3)), "first", "expected export object");
    }

    [Test]
    public async Task Replace_RejectsDifferentLogicalIdentityAndUnselectedExpectedObject()
    {
        var export = Export("selected");
        var builder = new ModuleExportRegistryBuilder<TestExport>().Add(export);
        await AssertInvalid(() => builder.Replace(export, Export("other")), "selected", "other");
        await AssertInvalid(() => builder.Replace(Export("missing"), Export("missing", 2)), "missing", "not currently selected");
        builder.Replace(export, export);
        await Assert.That(builder.Build().Exports.Single()).IsSameReferenceAs(export);
    }

    [Test]
    public async Task Build_MissingDependencyIdentifiesCompleteRequestPath()
    {
        var builder = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("root", requirements: [new("middle", 1)]))
            .Add(Export("middle", requirements: [new("missing", 1)]));
        await AssertInvalid(() => builder.Build(), "missing", "root -> middle -> missing");

        // A failed Build does not poison selection; explicitly adding the missing export repairs it.
        builder.Add(Export("missing"));
        await Assert.That(Names(builder.Build()).SequenceEqual(["missing", "middle", "root"])).IsTrue();
    }

    [Test]
    [Arguments(1, 2)]
    [Arguments(2, 1)]
    public async Task Build_RequiresExactContractRevision(int requiredRevision, int selectedRevision)
    {
        var builder = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("consumer", requirements: [new("dependency", requiredRevision)]))
            .Add(Export("dependency", selectedRevision));
        await AssertInvalid(() => builder.Build(), "consumer", "dependency", $"revision {requiredRevision}", $"revision is {selectedRevision}", "consumer -> dependency");
    }

    [Test]
    public async Task Build_SchemaIsIndependentOfModuleContractRevision()
    {
        var registry = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("consumer", requirements: [new("dependency", 17)]))
            .Add(Export("dependency", 17))
            .Build();
        await Assert.That(registry.Exports[0].Descriptor.ContractVersion).IsEqualTo(17);
        await Assert.That(registry.Exports.All(export => export.Descriptor.SchemaVersion == ModuleExportSchema.CurrentVersion)).IsTrue();
    }

    [Test]
    public async Task Build_RejectsUnsupportedPositiveSchemaWithModuleAndPath()
    {
        var future = new TestExport(new("future", 1, schemaVersion: ModuleExportSchema.CurrentVersion + 1));
        var builder = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("consumer", requirements: [new("future", 1)]))
            .Add(future);
        await AssertInvalid(() => builder.Build(), "future", "schema 2", "supported schema is 1", "consumer -> future");
    }

    [Test]
    public async Task Build_ValidatesRequiredCapabilityWithoutSubstitutingAnotherCapability()
    {
        var builder = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("consumer", requirements: [new("dependency", 1, ["features"])]))
            .Add(Export("dependency", capabilities: ["hosting"]));
        await AssertInvalid(() => builder.Build(), "consumer", "dependency", "features", "consumer -> dependency");
    }

    [Test]
    public async Task IdentitiesAndCapabilities_AreOrdinalAndCaseSensitive()
    {
        var registry = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("module", requirements: [new("Module", 1, ["Features"])]))
            .Add(Export("Module", capabilities: ["Features", "features"]))
            .Build();
        await Assert.That(Names(registry).SequenceEqual(["Module", "module"])).IsTrue();

        var wrongCase = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("consumer", requirements: [new("dependency", 1, ["Features"])]))
            .Add(Export("dependency", capabilities: ["features"]));
        await AssertInvalid(() => wrongCase.Build(), "Features", "consumer", "dependency");
    }

    [Test]
    public async Task Build_ReportsDependencyCycleIncludingTheClosingModule()
    {
        var builder = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("entry", requirements: [new("a", 1)]))
            .Add(Export("a", requirements: [new("b", 1)]))
            .Add(Export("b", requirements: [new("a", 1)]));
        await AssertInvalid(() => builder.Build(), "cycle", "a -> b -> a");
    }

    [Test]
    public async Task Build_ReportsSelfDependencyCycle()
    {
        var builder = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("self", requirements: [new("self", 1)]));
        await AssertInvalid(() => builder.Build(), "cycle", "self -> self");
    }

    [Test]
    public async Task Replace_RevalidatesFinalRequiredRevisionAndCapabilities()
    {
        var original = Export("dependency", capabilities: ["features"]);
        var builder = new ModuleExportRegistryBuilder<TestExport>()
            .Add(Export("consumer", requirements: [new("dependency", 1, ["features"])]))
            .Add(original);
        var previous = builder.Build();
        var incompatible = Export("dependency", 2, capabilities: ["features"]);
        builder.Replace(original, incompatible);
        await AssertInvalid(() => builder.Build(), "consumer", "dependency", "revision 1", "revision is 2");

        var missingCapability = Export("dependency");
        builder.Replace(incompatible, missingCapability);
        await AssertInvalid(() => builder.Build(), "consumer", "dependency", "features");
        await Assert.That(previous.Exports[0]).IsSameReferenceAs(original);
    }

    [Test]
    public async Task Replace_RevalidatesFinalSchemaAndDependencyGraph()
    {
        var original = Export("module");
        var builder = new ModuleExportRegistryBuilder<TestExport>().Add(original);
        var future = new TestExport(new("module", 1, schemaVersion: 2));
        builder.Replace(original, future);
        await AssertInvalid(() => builder.Build(), "module", "schema 2");

        var missing = Export("module", requirements: [new("missing", 1)]);
        builder.Replace(future, missing);
        await AssertInvalid(() => builder.Build(), "module -> missing");
    }

    [Test]
    public async Task RegistriesAndBuilderReuse_DoNotLeakHostSelectionsOrMutableCollections()
    {
        var sharedExport = Export("shared");
        var first = new ModuleExportRegistryBuilder<TestExport>().Add(sharedExport);
        var second = new ModuleExportRegistryBuilder<TestExport>().Add(sharedExport);
        var firstSnapshot = first.Build();
        var secondSnapshot = second.Build();
        first.Add(Export("first-only"));
        second.Replace(sharedExport, Export("shared", 2));

        await Assert.That(firstSnapshot.Exports.Count).IsEqualTo(1);
        await Assert.That(secondSnapshot.Exports.Single()).IsSameReferenceAs(sharedExport);
        await Assert.That(Names(first.Build()).SequenceEqual(["shared", "first-only"])).IsTrue();
        await Assert.That(second.Build().Exports.Single().Descriptor.ContractVersion).IsEqualTo(2);
        await Assert.That(() => ((IList<TestExport>)firstSnapshot.Exports).Clear()).Throws<NotSupportedException>();
        await Assert.That(new ModuleExportRegistryBuilder<TestExport>().Build().Exports.Count).IsEqualTo(0);
    }

    private static TestExport Export(string moduleId, int revision = 1, IEnumerable<ModuleRequirement>? requirements = null, IEnumerable<string>? capabilities = null)
        => new(new(moduleId, revision, requirements, capabilities));

    private static IEnumerable<string> Names(ModuleExportRegistry<TestExport> registry) => registry.Exports.Select(export => export.Descriptor.ModuleId);

    private static async Task AssertInvalid(Action action, params string[] fragments)
    {
        InvalidOperationException? failure = null;
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        foreach (var fragment in fragments)
            await Assert.That(failure!.Message.Contains(fragment, StringComparison.Ordinal)).IsTrue()
                .Because($"Expected '{fragment}' in '{failure.Message}'.");
    }

    private sealed class TestExport(ModuleExportDescriptor descriptor) : ModuleExport(descriptor);
}