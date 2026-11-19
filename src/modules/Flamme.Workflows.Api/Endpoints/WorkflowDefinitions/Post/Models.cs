using Flamme.Workflows.Api.Models;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Post;

internal record Response(LinkedWorkflowDefinitionModel WorkflowDefinition, bool AlreadyPublished, int ConsumingWorkflowCount, ICollection<string> ValidationErrors);