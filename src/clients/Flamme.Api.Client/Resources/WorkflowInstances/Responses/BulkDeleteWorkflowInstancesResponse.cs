using System.Text.Json.Serialization;

namespace Flamme.Api.Client.Resources.WorkflowInstances.Responses;

internal sealed class BulkDeleteWorkflowInstancesResponse
{
    [JsonPropertyName("deleted")] public int DeletedCount { get; }
}