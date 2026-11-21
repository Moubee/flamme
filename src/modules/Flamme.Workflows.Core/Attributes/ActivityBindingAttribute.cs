namespace Flamme.Workflows;

/// <summary>
/// Declares an exact activity type for generation in an assembly or an <see cref="ActivityContext"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ActivityBindingAttribute(Type activityType) : Attribute
{
    /// <summary>
    /// The concrete CLR activity type, including its type arguments if generic.
    /// </summary>
    public Type ActivityType { get; } = activityType;
}
