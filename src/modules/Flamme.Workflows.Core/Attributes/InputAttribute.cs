namespace Flamme.Workflows.Attributes;

/// <summary>
/// Specifies various metadata about an activity's input property.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class InputAttribute : Attribute
{
    /// <summary>
    /// The technical name to use for the input property.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The user-friendly name of the activity property.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// A brief description of the input property.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// A category to group this property with.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// A value to order this property by. Properties are displayed in ascending order (lower appears before higher).
    /// </summary>
    public float Order { get; set; }

    /// <summary>
    /// The default value to set.
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// A type that implements <see cref="IActivityPropertyDefaultValueProvider"/> and provides a default value. When specified, the <see cref="DefaultValue"/> will be ignored.
    /// </summary>
    public Type? DefaultValueProvider { get; set; }

    /// <summary>
    /// The syntax to use by default when evaluating the value. Only used when the property definition doesn't have a syntax specified. 
    /// </summary>
    public string? DefaultSyntax { get; set; }

    /// <summary>
    /// The syntax to use by default when evaluating the value. Only used when the property definition doesn't have a syntax specified. 
    /// </summary>
    public string[]? SupportedSyntaxes { get; set; }

    /// <summary>
    /// A value indicating whether this property should be displayed as read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// A value indicating whether this property should be visible.
    /// </summary>
    public bool IsBrowsable { get; set; } = true;

    /// <summary>
    /// True if the activity invoker should evaluate the expression, false otherwise.
    /// When set to false, it is up to the activity itself to evaluate its input before using it. 
    /// </summary>
    public bool AutoEvaluate { get; set; } = true;

    /// <summary>
    /// Specifies the type of a custom evaluator to use for evaluating the input property value.
    /// The evaluator type determines how the value for the property is resolved at runtime.
    /// </summary>
    public Type? EvaluatorType { get; set; }

    /// <summary>
    /// A value indicating whether this input can be serialized as part of the workflow instance,
    /// </summary>
    public bool IsSerializable { get; set; } = true;
    
    /// <summary>
    /// Gets or sets a value indicating whether this input can contain secrets.
    /// When set to true, the input will be treated as a secret and will be encrypted, masked or otherwise protected, depending on the configured policy.
    /// </summary>
    public bool CanContainSecrets { get; set; }
}