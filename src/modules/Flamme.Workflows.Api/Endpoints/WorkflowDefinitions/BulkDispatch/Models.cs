using System.Text.Json.Serialization;
using Flamme.Common.Models;
using Flamme.Workflows.Serialization.Converters;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.BulkDispatch;

internal class Request
{
    public string DefinitionId { get; set; } = default!;
    public VersionOptions? VersionOptions { get; set; }
    public string? TriggerActivityId { get; set; }

    [JsonConverter(typeof(ExpandoObjectConverterFactory))]
    public object? Input { get; set; }

    public int Count { get; set; } = 1;
}

internal record Response(ICollection<string> WorkflowInstanceIds);