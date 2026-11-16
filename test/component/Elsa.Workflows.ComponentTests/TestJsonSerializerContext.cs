using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Api.Endpoints.BookmarkQueueDeadLetters;
using Elsa.Workflows.Api.Endpoints.WorkflowDefinitions.Execute;
using Elsa.Workflows.Runtime;

namespace Elsa.Workflows.ComponentTests;

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
