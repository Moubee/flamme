using System.Text.Json.Serialization;
using Flamme.Workflows.Models;
using Flamme.Workflows.Serialization.Converters;

namespace Flamme.Workflows.Api.Endpoints.Events.TriggerAuthenticated;

internal class Request
{
    public string EventName { get; set; } = default!;
    public string? WorkflowInstanceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? ActivityInstanceId { get; set; }
    
    [JsonConverter(typeof(ExpandoObjectConverterFactory))]
    public object? Input { get; set; }

    public WorkflowExecutionMode WorkflowExecutionMode { get; set; } = WorkflowExecutionMode.Asynchronous;
}