namespace Elsa.Features.Services;

internal static class FeatureGraph
{
    internal static IReadOnlyList<Type> Sort(IEnumerable<Type> roots, Func<Type, IReadOnlyList<Type>> dependencies)
    {
        var ordered = new List<Type>();
        var visited = new HashSet<Type>();
        var path = new List<Type>();
        foreach (var root in roots)
            Visit(root);
        return ordered;

        void Visit(Type type)
        {
            var cycle = path.IndexOf(type);
            if (cycle >= 0)
                throw new InvalidOperationException($"Feature dependency cycle: {Format(path.Skip(cycle).Append(type))}.");
            if (!visited.Add(type))
                return;
            path.Add(type);
            IReadOnlyList<Type> next;
            try
            {
                next = dependencies(type);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"Invalid feature dependency path {Format(path)}: {exception.Message}", exception);
            }
            foreach (var dependency in next)
                Visit(dependency);
            path.RemoveAt(path.Count - 1);
            ordered.Add(type);
        }
    }

    internal static string Format(IEnumerable<Type> path) => string.Join(" -> ", path.Select(type => type.FullName ?? type.Name));
}