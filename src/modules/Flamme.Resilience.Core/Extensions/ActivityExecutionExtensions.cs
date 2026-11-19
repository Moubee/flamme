using System.Text.Json.Nodes;
using Flamme.Workflows;

namespace Flamme.Resilience.Extensions;

public static class ActivityExecutionExtensions
{
    private const string RetriesAttemptedFlag = "HasRetryAttempts";
    private const string ResilienceStrategy = "ResilienceStrategy";

    public static void SetRetriesAttemptedFlag(this ActivityExecutionContext context)
    {
        var current = context;

        while (current != null)
        {
            current.SetMetadata(RetriesAttemptedFlag, true);
            current = current.ParentActivityExecutionContext;
        }
    }

    public static bool GetRetriesAttemptedFlag(this ActivityExecutionContext context)
    {
        return context.GetMetadata(RetriesAttemptedFlag, () => false);
    }

    public static void SetResilienceStrategy(this ActivityExecutionContext context, JsonNode model)
    {
        context.SetProperty(ResilienceStrategy, model);
    }
}