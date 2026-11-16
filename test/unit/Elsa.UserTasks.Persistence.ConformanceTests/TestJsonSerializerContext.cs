using System.Text.Json.Serialization;
using Elsa.UserTasks.Models;

namespace Elsa.UserTasks.Persistence.ConformanceTests;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UserTaskGuestSession))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
