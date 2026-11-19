using Flamme.Resilience.Entities;
using Flamme.Workflows;

namespace Flamme.Resilience.Models;

public record RecordRetryAttemptsContext(ActivityExecutionContext ActivityExecutionContext, ICollection<RetryAttemptRecord> Attempts, CancellationToken CancellationToken);