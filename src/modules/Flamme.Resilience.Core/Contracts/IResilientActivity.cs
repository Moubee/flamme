using Flamme.Resilience.Models;
using Flamme.Workflows;

namespace Flamme.Resilience;

public interface IResilientActivity : IActivity
{
    IDictionary<string, string?> CollectRetryDetails(ActivityExecutionContext context, RetryAttempt attempt);
}