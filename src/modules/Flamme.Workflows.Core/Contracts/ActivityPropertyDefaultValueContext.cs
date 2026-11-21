namespace Flamme.Workflows;

/// <summary>
/// The exact activity and the CLR member receiving a default value.
/// </summary>
public readonly record struct ActivityPropertyDefaultValueContext(
    Type ActivityType,
    string ClrName,
    Type DeclaringType,
    Type PropertyType);
