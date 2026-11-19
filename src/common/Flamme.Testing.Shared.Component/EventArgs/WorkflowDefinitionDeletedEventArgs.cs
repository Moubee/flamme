namespace Flamme.Testing.Shared;

public class WorkflowDefinitionDeletedEventArgs(string definitionId) : EventArgs 
{
    public string DefinitionId { get; } = definitionId;
}