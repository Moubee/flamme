using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

internal sealed record WorkflowHttpInstance(string Id, string Correlation);

internal sealed class WorkflowHttpClient(HttpClient api)
{
    private const string EventName = "store-bench";

    public async Task PublishAsync(string definition)
    {
        var model = new
        {
            definitionId = definition,
            name = "Workflow store checkpoint benchmark",
            variables = Array.Empty<object>(),
            root = new
            {
                id = "wait",
                type = "Signals.Event",
                version = 1,
                eventName = new { typeName = "String", expression = new { type = "Literal", value = EventName } }
            }
        };
        using var response = await api.PostAsJsonAsync("workflow-definitions", new { model, publish = true });
        response.EnsureSuccessStatusCode();
    }

    public async Task<WorkflowHttpInstance> StartAsync(string definition, string correlation)
    {
        var route = $"workflow-definitions/{Uri.EscapeDataString(definition)}/execute";
        using var response = await api.PostAsJsonAsync(route, new { correlationId = correlation });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        var state = result?["workflowState"];
        if ((string?)state?["id"] is not { Length: > 0 } id ||
            (string?)state["subStatus"] != "Suspended" || (string?)state["correlationId"] != correlation)
            throw new InvalidDataException($"Expected a suspended workflow for correlation {correlation}.");
        return new WorkflowHttpInstance(id, correlation);
    }

    public async Task ResumeAsync(WorkflowHttpInstance instance)
    {
        var request = new
        {
            workflowInstanceId = instance.Id,
            correlationId = instance.Correlation,
            workflowExecutionMode = WorkflowExecutionMode.Synchronous
        };
        using var response = await api.PostAsJsonAsync($"events/{EventName}/trigger", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<byte[]> PrepareImportAsync(WorkflowHttpInstance instance, string newCorrelation)
    {
        using var response = await api.PostAsJsonAsync("bulk-actions/export/workflow-instances", new { ids = new[] { instance.Id } });
        response.EnsureSuccessStatusCode();
        var export = await response.Content.ReadFromJsonAsync<JsonObject>();
        if (export?["WorkflowState"] is not JsonObject state || (string?)state["id"] != instance.Id)
            throw new InvalidDataException($"Expected an export of instance {instance.Id}.");
        state["correlationId"] = newCorrelation;
        return JsonSerializer.SerializeToUtf8Bytes(new { WorkflowState = state });
    }

    public async Task ImportAsync(byte[] content)
    {
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipart.Add(file, "file", "state.json");
        using var response = await api.PostAsync("bulk-actions/import/workflow-instances", multipart);
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<JsonObject>();
        if ((int?)receipt?["imported"] != 1)
            throw new InvalidDataException("Expected the server to acknowledge one imported instance.");
    }
}
