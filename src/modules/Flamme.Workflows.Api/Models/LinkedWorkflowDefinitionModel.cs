using Flamme.Models;
using Flamme.Workflows.Management.Models;

namespace Flamme.Workflows.Api.Models;

public class LinkedWorkflowDefinitionModel(Link[]? links) : WorkflowDefinitionModel
{
    public Link[]? Links { get; init; } = links;
}
