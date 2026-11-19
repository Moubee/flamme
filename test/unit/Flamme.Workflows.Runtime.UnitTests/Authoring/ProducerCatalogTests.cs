using System.Security.Claims;
using Flamme.Workflows.Runtime.Authoring;

namespace Flamme.Workflows.Runtime.UnitTests.Authoring;

public class ProducerCatalogTests
{
    private static readonly ActivityRecipe North = new("north.mail", "Send", 1, "1.0.0", [new("receipt", RecipePrimitive.WriteLine, "North")]);
    private static readonly ActivityRecipe South = new("south.mail", "Send", 1, "1.0.0", [new("receipt", RecipePrimitive.WriteLine, "South")]);
    private static readonly NamespaceImport[] Imports = [new("north", "north.mail"), new("south", "south.mail")];

    [Test]
    public void RequiresAnAuthenticatedUnambiguousPrincipalWithAnExactGrant()
    {
        var catalog = CreateCatalog();
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => catalog.Admit(Principal("south"), North));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => catalog.Admit(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "north")])), North));
        var ambiguous = new ClaimsPrincipal(Principal("north").Identities.Concat(Principal("south").Identities));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => catalog.Admit(ambiguous, North));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => catalog.Admit(Principal("north"), North with { Namespace = "north.mail.sub" }));
    }

    [Test]
    public void RejectsDuplicateOverlappingAndReservedGrants()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ProducerCatalog([new("north", "north.mail"), new("south", "north.mail")]));
        Assert.ThrowsExactly<ArgumentException>(() => new ProducerCatalog([new("south", "north.mail.sub"), new("north", "north.mail")]));
        Assert.ThrowsExactly<ArgumentException>(() => new ProducerCatalog([new("outside", "controlflow.extensions")]));
    }

    [Test]
    public void AdmissionRejectsAmbiguousReleasesAndUnsupportedInstructions()
    {
        var catalog = CreateCatalog();
        catalog.Admit(Principal("north"), North);
        Assert.ThrowsExactly<ArgumentException>(() => catalog.Admit(Principal("north"), North with { Release = "1.1.0" }));
        Assert.ThrowsExactly<ArgumentException>(() => catalog.Admit(Principal("north"), North with { Major = 0 }));
        Assert.ThrowsExactly<ArgumentException>(() => catalog.Admit(Principal("north"), North with { Steps = [North.Steps[0], North.Steps[0]] }));
        Assert.ThrowsExactly<ArgumentException>(() => catalog.Admit(Principal("north"), North with { Steps = [new("opaque", (RecipePrimitive)17, "value")] }));
    }

    [Test]
    public void CompilationRejectsUnboundCallsAndConflicts()
    {
        var catalog = CreateCatalog();
        catalog.Admit(Principal("north"), North);
        var source = new AuthoredWorkflow("notices", Imports, [new("first", "north:Send", 1)]);
        Assert.ThrowsExactly<KeyNotFoundException>(() => CuratedWorkflowPublisher.Compile(catalog, source with { Calls = [new("first", "north:Send", 2)] }));
        Assert.ThrowsExactly<KeyNotFoundException>(() => CuratedWorkflowPublisher.Compile(catalog, source with { Calls = [new("first", "south:Send", 1)] }));
        Assert.ThrowsExactly<ArgumentException>(() => CuratedWorkflowPublisher.Compile(catalog, source with { Calls = [new("first", "Send", 1)] }));
        Assert.ThrowsExactly<ArgumentException>(() => CuratedWorkflowPublisher.Compile(catalog, source with { Calls = [source.Calls[0], source.Calls[0]] }));
        Assert.ThrowsExactly<ArgumentException>(() => CuratedWorkflowPublisher.Compile(catalog, source with { Imports = [Imports[0], new("north", "south.mail")] }));
        Assert.ThrowsExactly<KeyNotFoundException>(() => CuratedWorkflowPublisher.Compile(catalog, source with { Imports = [new("unused", "missing.scope")] }));
    }

    [Test]
    public async Task PinsIndependentReleasesAndStableOccurrenceIdsAcrossAliasChanges()
    {
        var catalog = CreateCatalog();
        var mutableSteps = North.Steps.ToArray();
        catalog.Admit(Principal("north"), North with { Steps = mutableSteps });
        catalog.Admit(Principal("south"), South);
        mutableSteps[0] = new("other", RecipePrimitive.WaitForEvent, "Changed after admission");

        var source = new AuthoredWorkflow("notices", Imports, [new("first", "north:Send", 1), new("second", "south:Send", 1), new("again", "north:Send", 1)]);
        var (root, manifest) = CuratedWorkflowPublisher.Compile(catalog, source);
        var (_, renamed) = CuratedWorkflowPublisher.Compile(catalog, source with
        {
            Imports = [new("n", "north.mail"), Imports[1]], Calls = [new("first", "n:Send", 1), source.Calls[1], new("again", "n:Send", 1)]
        });

        await Assert.That(root.Activities.Select(x => x.Id).ToArray()).IsEquivalentTo(["first.receipt", "second.receipt", "again.receipt"]);
        var releases = manifest.GetProperty("releases").EnumerateArray().ToArray();
        await Assert.That(releases.Length).IsEqualTo(2);
        await Assert.That(releases[0].GetProperty("key").GetString()).IsEqualTo("north.mail.Send");
        await Assert.That(releases[0].GetProperty("steps")[0].GetProperty("value").GetString()).IsEqualTo("North");
        await Assert.That(releases[0].GetProperty("digest").GetString()).IsNotEqualTo(releases[1].GetProperty("digest").GetString());
        var nodes = manifest.GetProperty("sourceMap");
        var renamedNodes = renamed.GetProperty("sourceMap");
        await Assert.That(nodes[2].GetProperty("activityId").GetString()).IsEqualTo(renamedNodes[2].GetProperty("activityId").GetString());
        await Assert.That(nodes[2].GetProperty("key").GetString()).IsEqualTo("north.mail.Send");
        await Assert.That(nodes[2].GetProperty("version").GetInt32()).IsEqualTo(1);
    }

    private static ProducerCatalog CreateCatalog() => new([new("north", "north.mail"), new("south", "south.mail")]);
    private static ClaimsPrincipal Principal(string subject) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], "test"));
}
