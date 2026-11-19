using System.Text.Json.Serialization;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.BulkDeleteVersions;

internal class Request
{
    public ICollection<string> Ids { get; set; } = default!;
}

internal class Response(long deletedCount)
{
    [JsonPropertyName("deleted")] public long DeletedCount { get; } = deletedCount;
}