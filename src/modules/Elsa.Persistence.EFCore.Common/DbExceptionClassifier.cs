namespace Elsa.Persistence.EFCore;

internal static class DbExceptionClassifier
{
    public static bool IsDuplicateKey(Exception exception)
    {
        return EnumerateExceptions(exception).Any(inner =>
            inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505");
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var stack = new Stack<Exception>();
        stack.Push(exception);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (current is AggregateException aggregateException)
            {
                foreach (var inner in aggregateException.InnerExceptions)
                    stack.Push(inner);
            }
            else if (current.InnerException is not null)
            {
                stack.Push(current.InnerException);
            }
        }
    }
}
