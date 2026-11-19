using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.Management;

public static class WorkflowDefinitionHandleExtensions
{
    public static WorkflowDefinitionFilter ToFilter(this WorkflowDefinitionHandle handle)
    {
        return new()
        {
            DefinitionId = handle.DefinitionId,
            Id = handle.DefinitionVersionId,
            VersionOptions = handle.VersionOptions
        };
    }
}