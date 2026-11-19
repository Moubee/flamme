using Flamme.Common.Models;
using Flamme.Resilience.Entities;

namespace Flamme.Resilience;

public interface IRetryAttemptReader
{
    Task<Page<RetryAttemptRecord>> ReadAttemptsAsync(string activityInstanceId, PageArgs? pageArgs = null, CancellationToken cancellationToken = default);   
}