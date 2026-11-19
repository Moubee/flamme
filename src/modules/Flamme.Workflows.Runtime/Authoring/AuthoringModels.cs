namespace Flamme.Workflows.Runtime.Authoring;

/// <summary>An administrator's assignment of a catalog namespace to an authenticated subject.</summary>
public sealed record NamespaceGrant(string Publisher, string Namespace);

/// <summary>A local name for one catalog namespace in a workflow source document.</summary>
public sealed record NamespaceImport(string Alias, string Namespace);

/// <summary>A stable occurrence ID, alias-qualified operation name, and exact public contract major.</summary>
public sealed record ActivityCall(string Id, string Name, int Major);

/// <summary>An ordered publication of calls to curated producer activities.</summary>
public sealed record AuthoredWorkflow(string DefinitionId, IReadOnlyList<NamespaceImport> Imports, IReadOnlyList<ActivityCall> Calls);

/// <summary>The literal-only instruction set accepted by this translator.</summary>
public enum RecipePrimitive
{
    WriteLine = 1,
    WaitForEvent = 2
}

/// <summary>The step ID is stable across releases and unique within its recipe.</summary>
public sealed record RecipeStep(string Id, RecipePrimitive Primitive, string Value);

/// <summary>A publisher's exact release of a public, versioned operation.</summary>
public sealed record ActivityRecipe(string Namespace, string Name, int Major, string Release, IReadOnlyList<RecipeStep> Steps);
