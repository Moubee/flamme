using Flamme.Expressions.Models;
using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <summary>
/// Direct access to a physical activity member, identified by its CLR name.
/// </summary>
public sealed record ActivityMemberBinding(
    string ClrName,
    Type DeclaringType,
    Type PropertyType,
    Func<IActivity, object?> GetValue,
    Action<IActivity, object?> SetValue)
{
    /// <summary>
    /// Creates a wrapped input from a literal when the activity omits a value.
    /// </summary>
    public Func<Literal, Input>? CreateInput { get; init; }
}
