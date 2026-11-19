using System.Security.Claims;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Runtime.Authoring;

internal static class Publish
{
    public static Task<WorkflowDefinition> NoticesAsync(CuratedWorkflowPublisher publisher, string definitionId, string release)
    {
        var catalog = new ProducerCatalog([new NamespaceGrant("org-north", "north.mail"), new("org-south", "south.mail")]);
        catalog.Admit(Principal("org-north"), new ActivityRecipe("north.mail", "Send", 1, release, [
            new("open", RecipePrimitive.WriteLine, "North opened"),
            new("approval", RecipePrimitive.WaitForEvent, "Approved"),
            new("receipt", RecipePrimitive.WriteLine, $"North receipt {release}")
        ]));
        catalog.Admit(Principal("org-south"), new ActivityRecipe("south.mail", "Send", 1, "1.0.0", [
            new("receipt", RecipePrimitive.WriteLine, "South receipt Elsa.WriteLine")
        ]));

        return publisher.PublishAsync(catalog, new AuthoredWorkflow(definitionId,
            [new("north", "north.mail"), new("south", "south.mail")],
            [new("customer", "north:Send", 1), new("billing", "south:Send", 1)]));
    }

    // The sample stands in for an authenticated host. Production supplies its verified principal.
    private static ClaimsPrincipal Principal(string subject) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], "sample"));
}
