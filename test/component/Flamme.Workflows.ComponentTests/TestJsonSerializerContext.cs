using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters;
using Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Execute;
using Flamme.Workflows.Runtime;

namespace Flamme.Workflows.ComponentTests;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BookmarkQueueDeadLetterModel))]
[JsonSerializable(typeof(PostRequest))]
[JsonSerializable(typeof(ExecuteWorkflowResult))]
[JsonSerializable(typeof(ExpandoObject))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(JsonElement))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
