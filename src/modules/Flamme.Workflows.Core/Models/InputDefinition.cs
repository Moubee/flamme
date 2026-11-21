using System.Text.Json.Serialization;

namespace Flamme.Workflows.Models;

/// <summary>
/// A definition of a workflow's input.
/// </summary>
public class InputDefinition : ArgumentDefinition
{
    /// <summary>
    /// Evaluate expressions in the entries of a dictionary input when a published workflow is used as an activity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EvaluateDictionaryEntries { get; set; }

    /// <summary>
    /// The type of the storage driver to use for this input.
    /// </summary>
    public Type? StorageDriverType { get; set; }
}