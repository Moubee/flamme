namespace Elsa.CodeGeneration.Abstractions.UnitTests;

public class ModuleTypeManifestTests
{
    [Test]
    public async Task ManifestSnapshotsClosedTypesForTheSelectedExport()
    {
        var types = new[] { typeof(string), typeof(List<int[]>) };
        var manifest = new ModuleTypeManifest(new("orders", 1), types);
        types[0] = typeof(int);

        var registry = new ModuleExportRegistryBuilder<ModuleTypeManifest>().Add(manifest).Add(manifest).Build();

        await Assert.That(registry.Exports.Single()).IsSameReferenceAs(manifest);
        await Assert.That(manifest.DeclaredTypes.SequenceEqual([typeof(string), typeof(List<int[]>)])).IsTrue();
        await Assert.That(() => ((IList<Type>)manifest.DeclaredTypes).Clear()).Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(typeof(List<>))]
    [Arguments(typeof(void))]
    [Arguments(typeof(int[]))]
    [Arguments(null)]
    public async Task ManifestRejectsEntriesOutsideTheClosedNamedTypeContract(Type? type)
    {
        await Assert.That(() => new ModuleTypeManifest(new("orders", 1), [type!])).Throws<ArgumentException>();
    }

    [Test]
    public async Task ManifestRejectsRepeatedTypes()
    {
        await Assert.That(() => new ModuleTypeManifest(new("orders", 1), [typeof(string), typeof(string)]))
            .Throws<ArgumentException>();
    }
}
