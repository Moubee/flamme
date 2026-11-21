using System.Text.Json.Serialization;

namespace Flamme.Api.Client.Resources.WorkflowDefinitions.Models;

/// <summary>
/// A definition of a workflow's input.
/// </summary>
public class InputDefinition : ArgumentDefinition
{
    /// <summary>
    /// Evaluate expressions in this dictionary input when the workflow runs as an activity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EvaluateDictionaryEntries { get; set; }

    /// <summary>
    /// The type of the storage driver to use for this input.
    /// </summary>
    public string? StorageDriverType { get; set; }
}
