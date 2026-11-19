using Flamme.Resilience.Models;

namespace Flamme.Resilience;

public interface IRetryAttemptRecorder
{
    Task RecordAsync(RecordRetryAttemptsContext context);
}