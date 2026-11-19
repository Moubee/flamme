using System.Text.Json.Serialization;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.BulkDelete;

internal class Request
{
    public ICollection<string> DefinitionIds { get; set; } = default!;
}

internal class Response(long deletedCount)
{
    [JsonPropertyName("deleted")] public long DeletedCount { get; } = deletedCount;
}