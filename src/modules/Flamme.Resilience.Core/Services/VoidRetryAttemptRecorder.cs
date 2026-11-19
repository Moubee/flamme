using Flamme.Resilience.Models;

namespace Flamme.Resilience;

public class VoidRetryAttemptRecorder : IRetryAttemptRecorder
{
    public static VoidRetryAttemptRecorder Instance { get; } = new();
    
    public Task RecordAsync(RecordRetryAttemptsContext context)
    {
        // Send records into the void.
        return Task.CompletedTask;
    }
}