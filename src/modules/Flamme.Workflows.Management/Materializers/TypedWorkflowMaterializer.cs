using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Mappers;

namespace Flamme.Workflows.Management.Materializers;

/// <summary>
/// A workflow materializer that deserializes workflows created in C# code.
/// </summary>
public class TypedWorkflowMaterializer(WorkflowDefinitionMapper workflowDefinitionMapper) : IWorkflowMaterializer
{
    /// <summary>
    /// The name of the materializer.
    /// </summary>
    public const string MaterializerName = "Typed";

    /// <inheritdoc />
    public string Name => MaterializerName;

    /// <inheritdoc />
    public ValueTask<Workflow> MaterializeAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var workflow = ToWorkflow(definition);
        return new ValueTask<Workflow>(workflow);
    }

    private Workflow ToWorkflow(WorkflowDefinition definition) => workflowDefinitionMapper.Map(definition);
}