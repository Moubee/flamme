using System.Collections.Immutable;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flamme.Workflows.Runtime.Authoring;

/// <summary>
/// An authoring host supplies trusted grants independently of submitted recipes and authenticates
/// each principal before admission. Each catalog selects one exact release per contract and major.
/// </summary>
public sealed partial class ProducerCatalog
{
    private static readonly HashSet<string> ReservedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alterations", "Authorization", "Console", "ControlFlow", "Data", "Http", "Signals",
        "Scripting", "System", "Tasks", "Time", "Workflows"
    };

    private readonly Dictionary<string, NamespaceGrant> _grants = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, int Major), AdmittedRecipe> _recipes = new();
    private readonly Lock _sync = new();

    public ProducerCatalog(IEnumerable<NamespaceGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        foreach (var grant in grants)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(grant.Publisher);
            ArgumentException.ThrowIfNullOrWhiteSpace(grant.Issuer);
            ArgumentException.ThrowIfNullOrWhiteSpace(grant.Namespace);
            var segments = grant.Namespace.Split('.');
            if (segments.Length < 2 || segments.Any(segment => !ScopeSegment().IsMatch(segment)) || ReservedRoots.Contains(segments[0]))
                throw new ArgumentException($"Invalid or reserved producer namespace '{grant.Namespace}'.", nameof(grants));

            if (_grants.Keys.Any(existing => existing == grant.Namespace ||
                existing.StartsWith(grant.Namespace + ".", StringComparison.Ordinal) || grant.Namespace.StartsWith(existing + ".", StringComparison.Ordinal)))
                throw new ArgumentException($"Duplicate or overlapping namespace grant '{grant.Namespace}'.", nameof(grants));
            _grants.Add(grant.Namespace, grant);
        }
    }

    /// <summary>Admits a copy of the recipe under the verified principal's exact namespace.</summary>
    public void Admit(ClaimsPrincipal principal, ActivityRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(recipe);
        var subjects = principal.Identities.Where(x => x.IsAuthenticated)
            .SelectMany(x => x.FindAll(ClaimTypes.NameIdentifier)).Select(x => (x.Issuer, x.Value)).Distinct().ToArray();
        if (subjects.Length != 1 || !_grants.TryGetValue(recipe.Namespace, out var grant) || subjects[0] != (grant.Issuer, grant.Publisher))
            throw new UnauthorizedAccessException($"The authenticated publisher has no grant for '{recipe.Namespace}'.");

        if (!IsIdentifier(recipe.Name) || recipe.Major < 1 || string.IsNullOrWhiteSpace(recipe.Release))
            throw new ArgumentException("An activity requires an operation name, positive contract major and exact release.", nameof(recipe));

        ArgumentNullException.ThrowIfNull(recipe.Steps);
        var steps = recipe.Steps.ToImmutableArray();
        if (steps.IsEmpty)
            throw new ArgumentException("A recipe requires at least one primitive.", nameof(recipe));
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (!IsIdentifier(step.Id) || !roles.Add(step.Id))
                throw new ArgumentException($"Invalid or duplicate recipe step ID '{step.Id}'.", nameof(recipe));
            if (!Enum.IsDefined(step.Primitive) || step.Value is null ||
                (step.Primitive == RecipePrimitive.WaitForEvent && string.IsNullOrWhiteSpace(step.Value)))
                throw new ArgumentException($"Invalid primitive or literal for recipe step '{step.Id}'.", nameof(recipe));
        }

        var key = $"{recipe.Namespace}.{recipe.Name}";
        var release = new AdmittedRecipe(grant.Issuer, grant.Publisher, key, recipe.Major, recipe.Release, steps);
        lock (_sync)
        {
            if (!_recipes.TryAdd((key, recipe.Major), release))
                throw new ArgumentException($"The catalog already binds '{key}' major {recipe.Major}.", nameof(recipe));
        }
    }

    internal bool HasNamespace(string name) => _grants.ContainsKey(name);

    internal AdmittedRecipe Resolve(string key, int major)
    {
        lock (_sync)
            return _recipes.TryGetValue((key, major), out var recipe) ? recipe :
                throw new KeyNotFoundException($"No admitted release of '{key}' major {major}.");
    }

    internal static bool IsIdentifier(string? value) => value != null && Identifier().IsMatch(value);

    [GeneratedRegex(@"\A[a-z][a-z0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeSegment();

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    internal sealed record AdmittedRecipe(string Issuer, string Publisher, string Key, int Major, string Release, ImmutableArray<RecipeStep> Steps)
    {
        public string Digest { get; } = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { Issuer, Publisher, Key, Major, Release, Steps })));
    }
}
