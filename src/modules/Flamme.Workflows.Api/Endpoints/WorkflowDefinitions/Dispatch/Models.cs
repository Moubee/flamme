using System.Text.Json.Serialization;
using Flamme.Common.Models;
using Flamme.Workflows.Serialization.Converters;

namespace Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Dispatch;

internal class Request
{
    public string DefinitionId { get; set; } = default!;
    public string? InstanceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? TriggerActivityId { get; set; }
    
    public VersionOptions? VersionOptions { get; set; }

    [JsonConverter(typeof(ExpandoObjectConverterFactory))]
    public object? Input { get; set; }

    public string? Channel { get; set; }
}

internal record Response(string WorkflowInstanceId);