using Flamme.Http.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Flamme.Http.Selectors;

/// <summary>
/// Returns the workflow instance ID from the <c>X-Workflow-Instance-ID</c> header, if any.
/// </summary>
public class QueryStringHttpWorkflowInstanceIdSelector : HttpWorkflowInstanceIdSelectorBase
{
    /// <inheritdoc />
    protected override string? GetWorkflowInstanceId(HttpContext httpContext)
    {
        return httpContext.Request.Query["workflowInstanceId"].FirstOrDefault();
    }
}