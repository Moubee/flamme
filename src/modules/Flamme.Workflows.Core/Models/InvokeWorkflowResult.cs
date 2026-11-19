using Flamme.Workflows.State;

namespace Flamme.Workflows.Models;

public record InvokeWorkflowResult(WorkflowState WorkflowState, ICollection<Bookmark> Bookmarks);