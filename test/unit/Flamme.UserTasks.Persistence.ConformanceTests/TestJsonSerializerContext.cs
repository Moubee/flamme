using System.Text.Json.Serialization;
using Flamme.UserTasks.Models;

namespace Flamme.UserTasks.Persistence.ConformanceTests;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UserTaskGuestSession))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
