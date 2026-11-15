namespace Elsa.Common.Services;

/// <inheritdoc />
public class SystemClock(TimeProvider timeProvider) : ISystemClock
{
    public SystemClock() : this(TimeProvider.System)
    {
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
