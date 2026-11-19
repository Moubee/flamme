using Flamme.Workflows.Management.Entities;

namespace Flamme.Workflows.Management.Models;

public record AffectedWorkflows(ICollection<WorkflowDefinition> WorkflowDefinitions);